using System.Runtime.InteropServices;
using ClipboardManager.App.ViewModels;
using ClipboardManager.Domain;
using ClipboardManager.Infrastructure;
using ClipboardManager.Infrastructure.Interop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ClipboardManager.App;

/// <summary>
/// Launcher 主窗口（规范 §24、§29）：无边框、Mica、置顶、底部居中 Dock 弹出。
/// 业务逻辑保持精简，全部委托 ViewModel（规范 §23）。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly nint _hwnd;

    public LauncherViewModel ViewModel { get; }

    /// <summary>窗口当前是否可见（用于快捷键切换显隐）。</summary>
    public bool IsVisible => NativeMethods.IsWindowVisible(_hwnd);

    /// <summary>请求隐藏（Esc / 粘贴后）。</summary>
    public event EventHandler? HideRequested;

    /// <summary>请求快速粘贴（Enter / 双击）。</summary>
    public event EventHandler? PasteRequested;

    /// <summary>请求打开设置窗口。</summary>
    public event EventHandler? SettingsRequested;

    public MainWindow(LauncherViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd));
        ConfigureWindow();

        // 点击窗口外区域自动隐藏（Paste 风格 launcher 行为）。
        Activated += OnWindowActivated;

        // 首次加载后依据现有分组重建标签栏。
        ((Microsoft.UI.Xaml.FrameworkElement)Content).Loaded += async (_, _) => await RefreshGroupTabsAsync();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            HideRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ConfigureWindow()
    {
        // 底部 Dock 弹窗（Paste 风格）：默认尺寸会在显示时按屏幕重新计算。
        // 高度按 DPI 换算（约 290 DIP），避免高缩放屏下截断。
        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var height = (int)Math.Round(290 * dpi / 96.0);
        NativeMethods.SetWindowPos(_hwnd, nint.Zero, 0, 0, 1200, height, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOMOVE);

        // 移除系统活动窗口描边（Win11 DWM 边框），失败则静默忽略（Win10 无此属性）。
        var borderColor = NativeMethods.DWMWA_COLOR_NONE;
        _ = NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));

        // 窗口 / 任务栏图标（Assets/app.ico 由 csproj 复制到输出目录）。
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
            {
                _appWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // 图标加载失败不影响启动。
        }

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        _appWindow.Closing += OnAppWindowClosing;
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // 关闭 = 隐藏，应用常驻托盘（规范 §31）。
        args.Cancel = true;
        sender.Hide();
    }

    /// <summary>
    /// 显示在底部居中，贴合屏幕下边缘（Paste 风格 Dock 栏）。
    /// 宽度按当前显示器工作区宽度自适应，高度固定。
    /// 禁止使用 Screen.PrimaryScreen（规范 §49）。
    /// </summary>
    public void PositionNearCursor()
    {
        NativeMethods.GetCursorPos(out var point);
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };

        if (monitor == nint.Zero || !NativeMethods.GetMonitorInfoW(monitor, ref info))
        {
            return;
        }

        var work = info.rcWork;

        // 窗口高度按 DPI 缩放：布局约需 290 DIP（头部 52 + 卡片 196 + 底部 32）。
        // SetWindowPos 使用物理像素，高缩放屏（125%/150%）下若不换算会截断卡片区。
        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var height = (int)Math.Round(290 * dpi / 96.0);
        const int margin = 24;

        // 宽度占满工作区，留出左右边距；最小 800，最大工作区宽度。
        var width = Math.Clamp(work.Right - work.Left - margin * 2, 800, work.Right - work.Left);

        // 底部居中。
        var x = work.Left + ((work.Right - work.Left - width) / 2);
        var y = work.Bottom - height - 12;

        NativeMethods.SetWindowPos(_hwnd, nint.Zero, x, y, width, height, NativeMethods.SWP_NOZORDER);
    }

    public void ShowWindow()
    {
        Activate();
        SearchBox.Focus(FocusState.Programmatic);
    }

    public void HideWindow()
    {
        _appWindow.Hide();
    }

    // ---- 键盘操作（规范 §29：↑↓ 选择、Enter 粘贴、Ctrl+C 复制、Esc 关闭）----

    private static bool IsCtrlDown()
        => Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            // 搜索框内回车一律执行搜索：有关键词立即搜索，空关键词刷新回最近历史。
            // 绝不在搜索框内触发"粘贴选中项"（避免误解为程序直接关闭）。
            var query = SearchBox.Text?.Trim() ?? string.Empty;
            _ = ViewModel.SearchNowAsync(query);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            HideRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.C && IsCtrlDown())
        {
            _ = ViewModel.CopySelectedAsync();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.G && IsCtrlDown())
        {
            // 搜索框内输入 G 会打字，分组需 Ctrl+G；列表聚焦时直接按 G。
            _ = ShowGroupDialogAsync();
            e.Handled = true;
        }
    }

    private void HistoryList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        => PasteRequested?.Invoke(this, EventArgs.Empty);

    private void HistoryList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            PasteRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            HideRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.C && IsCtrlDown())
        {
            _ = ViewModel.CopySelectedAsync();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.G)
        {
            _ = ShowGroupDialogAsync();
            e.Handled = true;
        }
    }

    // ---- 顶部标签栏 ----

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        // 重置所有标签为未选中样式（Paste 风格：中性 Surface 色）。
        var surface = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SurfaceBrush"];
        var secondary = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextSecondaryBrush"];
        foreach (var child in CategoryPanel.Children)
        {
            if (child is Button tab && tab != button)
            {
                tab.Background = surface;
                tab.Foreground = secondary;
            }
        }

        // 高亮当前标签：强调色。
        var accent = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentBrush"];
        button.Background = accent;
        button.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

        // 切换标签时同步清空搜索框；其挂起的防抖回调会被随后的新查询 CTS 取消。
        ViewModel.SearchText = string.Empty;

        // 自定义分组标签（Tag 存组名）→ 查询该分组内容。
        if (button.Tag is string groupName)
        {
            ViewModel.SearchText = string.Empty;
            _ = ViewModel.ShowGroupAsync(groupName);
            return;
        }

        // 标签切换全部重新查库，避免基于上一视图内存结果过滤导致数据错乱。
        switch (button.Content?.ToString())
        {
            case "收藏":
                _ = ViewModel.ShowFavoritesAsync();
                break;
            case "链接":
                _ = ViewModel.ShowTypeAsync(ClipboardType.Url);
                break;
            case "代码":
                _ = ViewModel.ShowTypeAsync(ClipboardType.Code);
                break;
            case "图片":
                _ = ViewModel.ShowTypeAsync(ClipboardType.Image);
                break;
            default:
                _ = ViewModel.ShowHistoryAsync();
                break;
        }
    }

    // ---- 工具栏 ----

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => SettingsRequested?.Invoke(this, EventArgs.Empty);

    // ---- 自定义分组标签栏（规范 §33：分组即标签） ----

    /// <summary>“+” 按钮：新建自定义分组标签。</summary>
    private async void AddGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var input = new TextBox { MinWidth = 300, PlaceholderText = "输入分组名称" };
        var dialog = new ContentDialog
        {
            Title = "新建分组标签",
            Content = input,
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var name = input.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        await RefreshGroupTabsAsync();
        HighlightTab(name);
    }

    /// <summary>
    /// 依据数据库现有分组重建标签栏中的动态标签（插入到 “+” 按钮之前）。
    /// </summary>
    public async Task RefreshGroupTabsAsync()
    {
        var groups = await ViewModel.GetGroupNamesAsync();

        var surface = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SurfaceBrush"];
        var secondary = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextSecondaryBrush"];
        var uiFont = (Microsoft.UI.Xaml.Media.FontFamily)Microsoft.UI.Xaml.Application.Current.Resources["FontFamilyUi"];

        // 移除已消失的分组标签。
        var stale = CategoryPanel.Children
            .OfType<Button>()
            .Where(b => b.Tag is string tag && !groups.Contains(tag))
            .ToList();
        foreach (var button in stale)
        {
            CategoryPanel.Children.Remove(button);
        }

        var addButton = CategoryPanel.Children.OfType<Button>().First(b => b.Name == "TabAddGroup");
        var insertIndex = CategoryPanel.Children.IndexOf(addButton);
        var existing = CategoryPanel.Children
            .OfType<Button>()
            .Where(b => b.Tag is string)
            .Select(b => (string)b.Tag!)
            .ToHashSet();

        foreach (var group in groups)
        {
            if (!existing.Add(group))
            {
                continue;
            }

            var tab = new Button
            {
                Content = group,
                Tag = group,
                Padding = new Microsoft.UI.Xaml.Thickness(14, 6, 14, 6),
                CornerRadius = new Microsoft.UI.Xaml.CornerRadius(16),
                Background = surface,
                Foreground = secondary,
                BorderThickness = new Microsoft.UI.Xaml.Thickness(0),
                FontFamily = uiFont,
                FontSize = 12,
            };
            tab.Click += Tab_Click;

            CategoryPanel.Children.Insert(Math.Min(insertIndex, CategoryPanel.Children.Count), tab);
            insertIndex++;
        }
    }

    /// <summary>按内容文本高亮指定标签（其余恢复中性色）。</summary>
    private void HighlightTab(string content)
    {
        var surface = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SurfaceBrush"];
        var secondary = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextSecondaryBrush"];
        var accent = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentBrush"];

        foreach (var child in CategoryPanel.Children)
        {
            if (child is not Button tab || tab.Name == "TabAddGroup")
            {
                continue;
            }

            var isActive = string.Equals(tab.Content?.ToString(), content, StringComparison.Ordinal);
            tab.Background = isActive ? accent : surface;
            tab.Foreground = isActive
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
                : secondary;
        }
    }

    // ---- 条目操作 ----

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardItemViewModel item)
        {
            _ = ViewModel.ToggleFavoriteAsync(item);
        }
    }

    /// <summary>分组按钮：对按钮所在卡片分组（未选中场景）。</summary>
    private void GroupButton_Click(object sender, RoutedEventArgs e)
    {
        // 按钮所在卡片若不是当前选中项，先选中它。
        if ((sender as FrameworkElement)?.DataContext is ClipboardItemViewModel item
            && ViewModel.SelectedItem != item)
        {
            ViewModel.SelectedItem = item;
        }

        _ = ShowGroupDialogAsync();
    }

    /// <summary>
    /// 分组对话框：选择现有分组或输入新分组名，已分组项可移出（规范 §33）。
    /// </summary>
    private async Task ShowGroupDialogAsync()
    {
        var item = ViewModel.SelectedItem;
        if (item is null)
        {
            return;
        }

        var existingGroups = await ViewModel.GetGroupNamesAsync();

        var combo = new ComboBox
        {
            MinWidth = 300,
            PlaceholderText = "选择现有分组",
            ItemsSource = existingGroups,
        };
        if (item.Item.GroupName is not null)
        {
            combo.SelectedItem = item.Item.GroupName;
        }

        var newGroupBox = new TextBox
        {
            MinWidth = 300,
            PlaceholderText = "或输入新分组名称",
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(combo);
        panel.Children.Add(newGroupBox);

        var dialog = new ContentDialog
        {
            Title = $"分组：{item.CardTitle}",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        if (item.Item.GroupName is not null)
        {
            dialog.SecondaryButtonText = "移出分组";
        }

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var name = string.IsNullOrWhiteSpace(newGroupBox.Text)
                ? combo.SelectedItem as string
                : newGroupBox.Text.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                await ViewModel.SetGroupAsync(item, name);
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await ViewModel.SetGroupAsync(item, null);
        }

        // 归组/移出可能产生新分组或清空分组，同步标签栏。
        _ = RefreshGroupTabsAsync();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardItemViewModel item)
        {
            _ = ViewModel.DeleteAsync(item);
        }
    }

    /// <summary>来源程序图标懒加载（UI 线程，规范 §22）。</summary>
    private void SourceIcon_Loaded(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardItemViewModel item)
        {
            item.EnsureSourceIcon();
        }
    }

    private void Thumbnail_Loaded(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardItemViewModel item)
        {
            item.EnsureThumbnail();
        }
    }
}
