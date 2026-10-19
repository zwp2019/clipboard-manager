using System.Runtime.InteropServices;
using ClipboardManager.App.Services;
using ClipboardManager.Infrastructure.Interop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace ClipboardManager.App.Controls;

/// <summary>A separate owned modal keeps the launcher geometry and selection intact.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The native Window owns the backdrop; OnClosed always disposes it.")]
public sealed partial class GlassDialogWindow : Window
{
    private readonly TaskCompletionSource<ContentDialogResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly nint _owner;
    private readonly nint _handle;
    private readonly Window _ownerWindow;
    private readonly GlassWindowBackdrop _backdrop;
    private readonly bool _hasPrimary;
    private bool _ownerWasEnabled;
    private bool _completed;

    private GlassDialogWindow(Window owner, ContentDialog description)
    {
        InitializeComponent();
        _ownerWindow = owner;
        _owner = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        _handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        DialogRoot.RequestedTheme = (owner.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default;
        DialogTitle.Text = description.Title?.ToString() ?? "剪贴板";
        Title = DialogTitle.Text;
        var content = description.Content;
        description.Content = null;
        if (content is ItemDetails) ContentScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        ContentHost.Content = content is string text
            ? new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 24 }
            : content;
        _hasPrimary = !string.IsNullOrWhiteSpace(description.PrimaryButtonText);
        PrimaryAction.Content = description.PrimaryButtonText;
        PrimaryAction.Visibility = _hasPrimary ? Visibility.Visible : Visibility.Collapsed;
        SecondaryAction.Content = description.SecondaryButtonText;
        SecondaryAction.Visibility = string.IsNullOrWhiteSpace(description.SecondaryButtonText) ? Visibility.Collapsed : Visibility.Visible;
        CloseAction.Content = string.IsNullOrWhiteSpace(description.CloseButtonText) ? "关闭" : description.CloseButtonText;
        var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_handle));
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        _ = NativeMethods.SetWindowOwner(_handle, -8 /* GWLP_HWNDPARENT */, _owner);
        Position(appWindow, content is ItemDetails ? 600 : 460);
        _backdrop = new GlassWindowBackdrop(this, DialogRoot);
        Closed += OnClosed;
        DialogRoot.Loaded += (_, _) =>
        {
            var input = FindTextBox(ContentHost);
            if (input is not null) input.Focus(FocusState.Programmatic);
            else CloseAction.Focus(FocusState.Programmatic);
        };
    }
    internal static async Task<ContentDialogResult> ShowAsync(Window owner, ContentDialog description)
    {
        var window = new GlassDialogWindow(owner, description);
        window._ownerWasEnabled = NativeMethods.IsWindowEnabled(window._owner);
        if (window._ownerWasEnabled) _ = NativeMethods.EnableWindow(window._owner, false);
        try { window.Activate(); return await window._result.Task; }
        finally { if (!window._completed) window.Close(); }
    }
    private void Position(AppWindow appWindow, int widthDip)
    {
        var bounds = new NativeMethods.RECT();
        if (!NativeMethods.GetWindowRect(_owner, ref bounds)) throw new InvalidOperationException("无法取得父窗口位置。");
        var point = new NativeMethods.POINT { X = (bounds.Left + bounds.Right) / 2, Y = (bounds.Top + bounds.Bottom) / 2 };
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfoW(monitor, ref info)) throw new InvalidOperationException("无法取得弹窗所在屏幕。");
        var dpi = NativeMethods.GetDpiForWindow(_owner);
        var scale = (dpi == 0 ? 96 : dpi) / 96.0;
        var work = info.rcWork;
        var width = Math.Min(widthDip, (work.Right - work.Left) / scale - 32);
        var maxHeight = Math.Min(680, (work.Bottom - work.Top) / scale - 40);
        ContentHost.Measure(new Size(Math.Max(200, width - 48), maxHeight - 150));
        var height = Math.Clamp(ContentHost.DesiredSize.Height + 148, Math.Min(240, maxHeight), maxHeight);
        var w = (int)Math.Round(width * scale); var h = (int)Math.Round(height * scale);
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32((work.Left + work.Right - w) / 2, (work.Top + work.Bottom - h) / 2, w, h));
        var corner = NativeMethods.DWMWCP_ROUND;
        if (NativeMethods.DwmSetWindowAttribute(_handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(uint)) != 0)
        {
            var radius = (int)Math.Round(22 * scale);
            var region = NativeMethods.CreateRoundRectRgn(0, 0, w + 1, h + 1, radius * 2, radius * 2);
            if (region != nint.Zero && NativeMethods.SetWindowRgn(_handle, region, true) == 0) NativeMethods.DeleteObject(region);
        }
        var border = NativeMethods.DWMWA_COLOR_NONE;
        _ = NativeMethods.DwmSetWindowAttribute(_handle, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(uint));
    }
    private void Finish(ContentDialogResult result)
    {
        if (_completed) return;
        _completed = true;
        RestoreOwner();
        _result.TrySetResult(result);
        Close();
    }
    private void OnClosed(object sender, WindowEventArgs args)
    {
        _backdrop.Dispose();
        if (!_completed) { _completed = true; RestoreOwner(); _result.TrySetResult(ContentDialogResult.None); }
    }
    private void RestoreOwner()
    {
        if (_ownerWasEnabled && NativeMethods.IsWindow(_owner))
        { _ = NativeMethods.EnableWindow(_owner, true); _ownerWindow.Activate(); _ownerWasEnabled = false; }
    }
    private void Primary_Click(object sender, RoutedEventArgs e) => Finish(ContentDialogResult.Primary);
    private void Secondary_Click(object sender, RoutedEventArgs e) => Finish(ContentDialogResult.Secondary);
    private void Close_Click(object sender, RoutedEventArgs e) => Finish(ContentDialogResult.None);
    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { e.Handled = true; Finish(ContentDialogResult.None); }
        else if (e.Key == VirtualKey.Enter && _hasPrimary && FocusManager.GetFocusedElement(DialogRoot.XamlRoot) is TextBox { AcceptsReturn: false })
        { e.Handled = true; Finish(ContentDialogResult.Primary); }
    }
    private static TextBox? FindTextBox(DependencyObject parent)
    {
        if (parent is TextBox box) return box;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = FindTextBox(VisualTreeHelper.GetChild(parent, i)); if (child is not null) return child; }
        return null;
    }
}
