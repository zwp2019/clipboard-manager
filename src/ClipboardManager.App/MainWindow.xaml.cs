using System.Runtime.InteropServices;
using ClipboardManager.App.ViewModels;
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
/// Launcher 主窗口（规范 §24、§29）：无边框、Mica、置顶、快捷键操作。
/// 业务逻辑保持精简，全部委托 ViewModel（规范 §23）。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly nint _hwnd;

    public LauncherViewModel ViewModel { get; }

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
    }

    private void ConfigureWindow()
    {
        _appWindow.Resize(new SizeInt32(560, 720));

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        SystemBackdrop = new MicaBackdrop();
        _appWindow.Closing += OnAppWindowClosing;
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // 关闭 = 隐藏，应用常驻托盘（规范 §31）。
        args.Cancel = true;
        sender.Hide();
    }

    /// <summary>
    /// 显示在鼠标所在显示器的工作区中心（规范 §49，禁止使用 Screen.PrimaryScreen）。
    /// </summary>
    public void PositionOnCursorMonitor()
    {
        NativeMethods.GetCursorPos(out var point);
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };

        if (monitor != nint.Zero && NativeMethods.GetMonitorInfoW(monitor, ref info))
        {
            var work = info.rcWork;
            var width = _appWindow.Size.Width;
            var height = _appWindow.Size.Height;
            var x = work.Left + (work.Right - work.Left - width) / 2;
            var y = work.Top + (work.Bottom - work.Top - height) / 2;
            _appWindow.Move(new PointInt32(x, y));
        }
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

    // ---- 键盘操作（规范 §29：↑↓ 选择、Enter 粘贴、Esc 关闭）----

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        => PasteRequested?.Invoke(this, EventArgs.Empty);

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            HideRequested?.Invoke(this, EventArgs.Empty);
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
    }

    // ---- 底部工具栏 ----

    private void FavoritesButton_Click(object sender, RoutedEventArgs e)
        => _ = ViewModel.ShowFavoritesAsync();

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
        => _ = ViewModel.ShowHistoryAsync();

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => SettingsRequested?.Invoke(this, EventArgs.Empty);

    // ---- 条目操作 ----

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardItemViewModel item)
        {
            _ = ViewModel.ToggleFavoriteAsync(item);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardItemViewModel item)
        {
            _ = ViewModel.DeleteAsync(item);
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
