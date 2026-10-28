using System.Runtime.InteropServices;
using ClipboardManager.App.Services;
using ClipboardManager.App.ViewModels;
using ClipboardManager.Domain;
using ClipboardManager.Infrastructure;
using ClipboardManager.Infrastructure.Interop;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using WinRT;

namespace ClipboardManager.App;

/// <summary>
/// Launcher 主窗口（规范 §24、§29）：无边框、Mica、置顶、底部居中 Dock 弹出。
/// 业务逻辑保持精简，全部委托 ViewModel（规范 §23）。
/// </summary>
public sealed partial class MainWindow : Window, IDisposable
{
    private const int DockHeight = 296;
    private readonly AppWindow _appWindow;
    private readonly nint _hwnd;

    /// <summary>DWM 圆角属性是否可用（Win11 为 true）；false 表示 Win10，需用区域裁剪圆角。</summary>
    private bool _legacyRoundedCorners;

    /// <summary>Acrylic 毛玻璃控制器与配置（KeepAlive 防止被 GC 回收后效果消失）。</summary>
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfiguration;

    /// <summary>窗口外点击检测（低级鼠标钩子），窗口显示期间启用。</summary>
    private readonly OutsideClickDetector _outsideClickDetector;

    public LauncherViewModel ViewModel { get; }

    /// <summary>窗口当前是否可见（用于快捷键切换显隐）。</summary>
    public bool IsVisible => NativeMethods.IsWindowVisible(_hwnd);

    /// <summary>请求隐藏（Esc / 复制后）。</summary>
    public event EventHandler? HideRequested;

    /// <summary>请求复制当前卡片并收起（Enter / Ctrl+C / 双击）。</summary>
    public event EventHandler? CopyRequested;

    /// <summary>请求打开设置窗口。</summary>
    public event EventHandler? SettingsRequested;

    public MainWindow(LauncherViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        ViewModel.CopyFeedback += ShowCopyFeedback;
        RootGrid.SizeChanged += (_, _) => NavigationCluster.MaxWidth = Math.Max(120, RootGrid.ActualWidth - 112);

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd));
        _outsideClickDetector = new OutsideClickDetector(() => { if (!IsInteractionOpen && !App.IsPreview) HideRequested?.Invoke(this, EventArgs.Empty); });
        ConfigureWindow();
        RootGrid.ActualThemeChanged += (_, _) => { UpdateBackdropTheme(); HighlightTab(_highlighted); };
        UpdateBackdropTheme();

        // 点击窗口外区域自动隐藏（Paste 风格 launcher 行为）：
        // 失焦事件兜底（Alt+Tab 等），窗口外点击由低级鼠标钩子精确覆盖（任务栏/桌面等不触发失焦的场景）。
        Activated += OnWindowActivated;

        // 全局键盘导航：左右箭头在卡片间移动（handledEventsToo 以覆盖 TextBox 已消费的光标键）。
        RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnGlobalKeyDown), true);
        // Handle Escape throughout the panel, and card confirmation before
        // ListViewItem can consume Enter.
        RootGrid.PreviewKeyDown += OnPreviewKeyDown;
        HistoryList.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(HistoryList_KeyDown), true);
        RootGrid.CharacterReceived += RootGrid_CharacterReceived;
        TrackFlyout(OverflowButton.Flyout);

        // 首次加载后依据现有分组重建标签栏。
        ((Microsoft.UI.Xaml.FrameworkElement)Content).Loaded += async (_, _) => await RefreshGroupTabsAsync();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_dismissTask is not null) return;
        if (args.WindowActivationState == WindowActivationState.Deactivated && !IsInteractionOpen && !App.IsPreview)
        {
            _outsideClickDetector.Stop();
            HideRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (!IsInteractionOpen)
        {
            // 窗口显示（启动激活 / 热键唤起 / 托盘打开）即开始监听窗口外点击。
            _outsideClickDetector.Start(_hwnd);
        }
    }

    private void ConfigureWindow()
    {
        // 底部 Dock 弹窗（Paste 风格）：默认尺寸会在显示时按屏幕重新计算。
        // Keep the compact strip in DIPs so physical pixels do not inflate the UI.
        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var height = (int)Math.Round(DockHeight * dpi / 96.0);
        NativeMethods.SetWindowPos(_hwnd, nint.Zero, 0, 0, 1200, height, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOMOVE);

        // 移除系统活动窗口描边（Win11 DWM 边框），失败则静默忽略（Win10 无此属性）。
        var borderColor = NativeMethods.DWMWA_COLOR_NONE;
        _ = NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));

        // 窗口圆角：Win11 走 DWM；失败（Win10 无此属性）则回退为区域裁剪圆角并清除系统浅色 1px 边框。
        var cornerPreference = NativeMethods.DWMWCP_ROUND;
        var dwmResult = NativeMethods.DwmSetWindowAttribute(
            _hwnd,
            NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref cornerPreference,
            sizeof(uint));

        _legacyRoundedCorners = dwmResult != 0;
        if (_legacyRoundedCorners)
        {
            ApplyLegacyRoundedCorners(1200, height, dpi);
        }

        // Desktop Acrylic blurs the windows behind this transient launcher.
        TryEnableAcrylic();

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
    /// Initialize both dispatcher queues before creating the native backdrop.
    /// Log initialization failures instead of silently presenting a solid panel as acrylic.
    /// </summary>
    private void TryEnableAcrylic()
    {
        try
        {
            if (!DesktopAcrylicController.IsSupported() || new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
            {
                Serilog.Log.Information("Launcher acrylic unavailable or high contrast enabled; using solid background");
                return;
            }
            // Microsoft.UI.Dispatching and Windows.System queues are distinct.
            // The composition system backdrop requires the latter as well.
            DispatcherQueue.EnsureSystemDispatcherQueue();
            _acrylicController = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
            _backdropConfiguration = new SystemBackdropConfiguration
            {
                IsInputActive = true,
                Theme = RootGrid.ActualTheme == ElementTheme.Light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark,
            };
            if (!_acrylicController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>()))
                throw new InvalidOperationException("Unable to attach the launcher acrylic backdrop.");
            _acrylicController.SetSystemBackdropConfiguration(_backdropConfiguration);

            RootGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            Serilog.Log.Information("Launcher acrylic initialized; system advanced effects enabled: {Enabled}", new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled);
        }
        catch (Exception ex)
        {
            _acrylicController?.Dispose();
            _acrylicController = null;
            _backdropConfiguration = null;
            RootGrid.Background = ThemeBrush("BackgroundBrush");
            Serilog.Log.Warning(ex, "Launcher acrylic initialization failed; using solid background");
        }
    }

    /// <summary>
    /// Win10 兼容圆角：DWM 圆角属性仅 Win11 有效，Win10 用区域裁剪实现圆角，
    /// 并移除系统默认的浅色 1px 窗口边框（DWMWA_BORDER_COLOR 在 Win10 同样无效）。
    /// 注意：区域裁剪会使 DWM 不再绘制窗口投影。
    /// </summary>
    private void ApplyLegacyRoundedCorners(int width, int height, uint dpi)
    {
        // 圆角半径按 DPI 换算（约 12 DIP）。
        var radius = Math.Max(8, (int)Math.Round(20 * dpi / 96.0));
        var region = NativeMethods.CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (region == nint.Zero)
        {
            return;
        }

        // 失败（返回 0，系统未接管该区域）时圆角不生效，但不影响功能，静默忽略。
        _ = NativeMethods.SetWindowRgn(_hwnd, region, true);

        // 移除系统 1px 浅色边框（GWL_STYLE 低 32 位足够容纳窗口样式标志）。
        var style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_STYLE);
        _ = NativeMethods.SetWindowLong(
            _hwnd,
            NativeMethods.GWL_STYLE,
            style & ~(NativeMethods.WS_BORDER | NativeMethods.WS_DLGFRAME | NativeMethods.WS_THICKFRAME));
    }

    /// <summary>释放 Acrylic 毛玻璃控制器（应用退出时由 App 调用）。</summary>
    public void Dispose()
    {
        CancelDismissAnimation();
        ViewModel.CopyFeedback -= ShowCopyFeedback;
        _copyNoticeTimer?.Stop();
        _acrylicController?.Dispose();
        _acrylicController = null;
        _outsideClickDetector.Stop();
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

        // Navigation 56 + card strip 222 + quiet status 18 + outer border.
        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var scale = dpi / 96.0;
        var height = (int)Math.Round(DockHeight * scale);

        // 宽度占满整个工作区（横向铺满，无侧边间隙）。
        var inset = Math.Max(8, (int)Math.Round(8 * scale));
        height = Math.Min(height, work.Bottom - work.Top - inset * 2);
        var width = work.Right - work.Left - inset * 2;

        // 贴底居中。
        var x = work.Left + inset;
        var y = work.Bottom - height - inset;

        NativeMethods.SetWindowPos(_hwnd, nint.Zero, x, y, width, height, NativeMethods.SWP_NOZORDER);

        // Win10：窗口尺寸变化后重新应用区域圆角。
        if (_legacyRoundedCorners)
        {
            ApplyLegacyRoundedCorners(width, height, dpi);
        }
    }

    public void ShowWindow()
    {
        CancelDismissAnimation();
        Activate();
        if (_searchOpen) SearchBox.Focus(FocusState.Programmatic);
        else HistoryList.Focus(FocusState.Programmatic);
    }

    public async Task RefreshForOpeningAsync()
    {
        await ViewModel.RefreshAsync();
        var first = ViewModel.Items.FirstOrDefault();
        ViewModel.SelectedItem = first;
        if (first is not null) HistoryList.ScrollIntoView(first);
    }

    public void HideWindow()
    {
        CancelDismissAnimation();
        _copyNoticeTimer?.Stop();
        CopyNotice.IsOpen = false;
        _outsideClickDetector.Stop();
        _appWindow.Hide();
    }

}
