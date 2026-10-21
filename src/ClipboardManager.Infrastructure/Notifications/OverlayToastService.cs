using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using ClipboardManager.Infrastructure.Interop;
using Microsoft.Extensions.Logging;
using Windows.UI.ViewManagement;

namespace ClipboardManager.Infrastructure.Notifications;

/// <summary>
/// 全局轻提示：跟随应用主题的半透明浮动卡片、内容类型图标和两行摘要，
/// 屏幕底部居中淡入淡出；置顶、不抢焦点、不接收输入（分层窗口自绘实现）。
/// 通知失败只记录日志，绝不影响剪贴板主流程（规范 §45）。
/// </summary>
public sealed partial class OverlayToastService : INotificationService, IDisposable
{
    private const int MaxPreviewLength = 80;
    private const int VisibleMilliseconds = 1200;
    private const int FadeStepMilliseconds = 22;
    private const byte MaxAlpha = 255;

    private readonly IDispatcher _dispatcher;
    private readonly ILogger<OverlayToastService> _logger;
    private readonly ISettingsService _settings;
    private readonly object _gate = new();
    private CancellationTokenSource? _showCts;

    private nint _hwnd;
    private bool _windowReady;
    private ClipboardType _contentType;
    private bool _dark;
    private bool _highContrast;

    public OverlayToastService(IDispatcher dispatcher, ILogger<OverlayToastService> logger, ISettingsService settings)
    {
        _dispatcher = dispatcher;
        _logger = logger;
        _settings = settings;
    }

    public void ShowItemCaptured(ClipboardItem item) => Show(item, true);

    public void ShowCopyResult(ClipboardItem item, bool success) => Show(item, success);

    private void Show(ClipboardItem item, bool success)
    {
        var text = BuildPreview(item);
        // 绘制窗口属于主线程（消息泵所在），所有 UI 操作调度回主线程。
        _ = _dispatcher.PostAsync(() =>
        {
            try
            {
                EnsureWindow();
                _ = ShowCoreAsync(text, item.Type, success);
            }
            catch (Exception ex)
            {
                Log.ShowToastFailed(_logger, ex);
            }
        });
    }

    private async Task ShowCoreAsync(string text, ClipboardType type, bool success)
    {
        if (_hwnd == nint.Zero)
        {
            return;
        }

        // 新提示替换旧提示（ElementUI 单例模式）。
        using var cancellation = new CancellationTokenSource();
        lock (_gate)
        {
            _showCts?.Cancel();
            _showCts = cancellation;
        }

        var token = cancellation.Token;

        try
        {
            _contentType = type;
            _highContrast = new AccessibilitySettings().HighContrast;
            var ui = new UISettings();
            var background = ui.GetColorValue(UIColorType.Background);
            _dark = _settings.Current.Theme switch
            {
                AppTheme.Dark => true,
                AppTheme.Light => false,
                _ => background.R + background.G + background.B < 384,
            };
            if (!ui.AnimationsEnabled)
            {
                Render(text, MaxAlpha, success);
                ShowWindowAtBottom();
                await Task.Delay(success ? VisibleMilliseconds : 2800, token);
                ShowWindow(_hwnd, SW_HIDE);
                return;
            }
            Render(text, 0, success);
            ShowWindowAtBottom();

            for (var step = 1; step <= 5; step++)
            {
                token.ThrowIfCancellationRequested();
                Render(text, (byte)(MaxAlpha * step / 5), success);
                ShowWindowAtBottom();
                await Task.Delay(18, token);
            }

            await Task.Delay(success ? VisibleMilliseconds : 2800, token);

            // 淡出：8 步 alpha 递减。
            for (var step = 7; step >= 0; step--)
            {
                token.ThrowIfCancellationRequested();
                Render(text, (byte)(MaxAlpha * step / 7), success);
                await Task.Delay(FadeStepMilliseconds, token);
            }

            ShowWindow(_hwnd, SW_HIDE);
        }
        catch (OperationCanceledException)
        {
            // 被新提示替换。
        }
        catch (Exception ex)
        {
            ShowWindow(_hwnd, SW_HIDE);
            Log.ShowToastFailed(_logger, ex);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_showCts, cancellation)) _showCts = null;
            }
        }
    }

    private void EnsureWindow()
    {
        if (_windowReady)
        {
            return;
        }

        var style = WS_POPUP;
        var exStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
        _hwnd = CreateWindowExW(exStyle, "STATIC", nint.Zero, style, 0, 0, 10, 10, nint.Zero, nint.Zero, nint.Zero, nint.Zero);
        _windowReady = _hwnd != nint.Zero;

        if (!_windowReady)
        {
            Log.OverlayWindowCreateFailed(_logger);
        }
    }

    private void Render(string text, byte alpha, bool success)
    {
        var dpi = GetDpiForWindow(_hwnd);
        var scale = (dpi == 0 ? 96 : dpi) / 96f;
        using var bitmap = CopyToastRenderer.Render(text, _contentType, success, _dark, scale, _highContrast);
        Present(bitmap, alpha);
    }

    private void Present(Bitmap bitmap, byte alpha)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        // 分层窗口提交（premultiplied alpha）。
        var screenDc = GetDC(nint.Zero);
        try
        {
            var memDc = CreateCompatibleDC(screenDc);
            try
            {
                var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                var original = SelectObject(memDc, hBitmap);
                try
                {
                    // Preserve the positioned toast during every fade frame.
                    // A default (0, 0) destination would jump it to the screen corner.
                    GetWindowRect(_hwnd, out var currentRect);
                    var windowPos = new NativePoint { X = currentRect.Left, Y = currentRect.Top };
                    var windowSize = new NativeSize(width, height);
                    var srcPos = default(NativePoint);
                    var blend = new BlendFunction
                    {
                        BlendOp = 0, // AC_SRC_OVER
                        SourceConstantAlpha = alpha,
                        AlphaFormat = 1, // AC_SRC_ALPHA
                    };

                    UpdateLayeredWindow(
                        _hwnd, screenDc, ref windowPos, ref windowSize, memDc, ref srcPos, 0, ref blend, 2 /* ULW_ALPHA */);

                }
                finally
                {
                    SelectObject(memDc, original);
                    DeleteObject(hBitmap);
                }
            }
            finally
            {
                DeleteDC(memDc);
            }
        }
        finally
        {
            // 返回值无需处理，仅归还屏幕 DC。
            _ = ReleaseDC(nint.Zero, screenDc);
        }
    }

    private void ShowWindowAtBottom()
    {
        var dpi = GetDpiForWindow(_hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var scale = dpi / 96f;
        NativeMethods.GetCursorPos(out var point);
        var foreground = NativeMethods.GetForegroundWindow();
        var targetRect = default(NativeMethods.RECT);
        if (foreground != nint.Zero && NativeMethods.GetWindowRect(foreground, ref targetRect))
        {
            point.X = targetRect.Left + (targetRect.Right - targetRect.Left) / 2;
            point.Y = targetRect.Top + (targetRect.Bottom - targetRect.Top) / 2;
        }
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfoW(monitor, ref info)) return;
        var work = info.rcWork;
        var offsetBottom = (int)(24 * scale);

        GetWindowRect(_hwnd, out var windowRect);
        var x = work.Left + ((work.Right - work.Left) - (windowRect.Right - windowRect.Left)) / 2;
        var y = work.Bottom - offsetBottom - (windowRect.Bottom - windowRect.Top);

        SetWindowPos(
            _hwnd, nint.Zero, x, y, 0, 0,
            SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private static string BuildPreview(ClipboardItem item)
    {
        var preview = item.Type == ClipboardType.Image
            ? string.IsNullOrWhiteSpace(item.Title) ? "图片" : $"图片 · {item.Title}"
            : item.GetPreviewText();
        if (item.Type == ClipboardType.File && !string.IsNullOrWhiteSpace(item.FilePaths))
        {
            try
            {
                var paths = JsonSerializer.Deserialize<string[]>(item.FilePaths);
                if (paths is { Length: > 0 })
                    preview = paths.Length == 1 ? Path.GetFileName(paths[0]) : $"{Path.GetFileName(paths[0])} 等 {paths.Length} 个文件";
            }
            catch (JsonException) { preview = "文件"; }
        }
        if (string.IsNullOrWhiteSpace(preview))
        {
            preview = "新内容";
        }

        preview = preview.ReplaceLineEndings(" ").Replace('\t', ' ').Trim();
        return preview.Length <= MaxPreviewLength ? preview : preview[..MaxPreviewLength] + "…";
    }

    // ---- Win32 interop（仅本类使用） ----

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const int SW_HIDE = 0;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;

        public NativeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(
        uint exStyle, string className, nint windowName, uint style,
        int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(
        nint hWnd, nint hdcDst, ref NativePoint pptDst, ref NativeSize pSize,
        nint hdcSrc, ref NativePoint pptSrc, uint crKey, ref BlendFunction blend, uint flags);

    public void Dispose()
    {
        _showCts?.Cancel();
    }

    /// <summary>
    /// 源生成日志方法，避免 CA1848/CA1873。
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(EventId = 0, Level = LogLevel.Warning, Message = "Failed to show capture toast")]
        public static partial void ShowToastFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to create toast overlay window")]
        public static partial void OverlayWindowCreateFailed(ILogger logger);
    }
}
