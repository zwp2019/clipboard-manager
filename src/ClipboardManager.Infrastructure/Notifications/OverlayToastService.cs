using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Infrastructure.Notifications;

/// <summary>
/// 全局轻提示（ElementUI Toast 风格）：半透明黑胶囊 + 绿色对勾 + 内容预览，
/// 屏幕底部居中淡入淡出；置顶、不抢焦点、不接收输入（分层窗口自绘实现）。
/// 通知失败只记录日志，绝不影响剪贴板主流程（规范 §45）。
/// </summary>
public sealed partial class OverlayToastService : INotificationService, IDisposable
{
    private const int MaxPreviewLength = 80;
    private const int VisibleMilliseconds = 1500;
    private const int FadeStepMilliseconds = 30;
    private const byte MaxAlpha = 242;

    private readonly IDispatcher _dispatcher;
    private readonly ILogger<OverlayToastService> _logger;
    private readonly object _gate = new();
    private CancellationTokenSource? _showCts;

    private nint _hwnd;
    private bool _windowReady;

    public OverlayToastService(IDispatcher dispatcher, ILogger<OverlayToastService> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public void ShowItemCaptured(ClipboardItem item)
    {
        var text = BuildPreview(item);

        // 绘制窗口属于主线程（消息泵所在），所有 UI 操作调度回主线程。
        _ = _dispatcher.PostAsync(() =>
        {
            try
            {
                EnsureWindow();
                _ = ShowCoreAsync(text);
            }
            catch (Exception ex)
            {
                Log.ShowToastFailed(_logger, ex);
            }
        });
    }

    private async Task ShowCoreAsync(string text)
    {
        if (_hwnd == nint.Zero)
        {
            return;
        }

        // 新提示替换旧提示（ElementUI 单例模式）。
        lock (_gate)
        {
            _showCts?.Cancel();
            _showCts = new CancellationTokenSource();
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_showCts.Token);
        var token = linked.Token;

        try
        {
            Render(text, MaxAlpha);
            ShowWindowAtBottom();

            await Task.Delay(VisibleMilliseconds, token);

            // 淡出：8 步 alpha 递减。
            for (var step = 7; step >= 0; step--)
            {
                token.ThrowIfCancellationRequested();
                Render(text, (byte)(MaxAlpha * step / 7));
                await Task.Delay(FadeStepMilliseconds, token);
            }

            ShowWindow(_hwnd, SW_HIDE);
        }
        catch (OperationCanceledException)
        {
            // 被新提示替换。
        }
    }

    private void EnsureWindow()
    {
        if (_windowReady)
        {
            return;
        }

        var style = WS_POPUP;
        var exStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
        _hwnd = CreateWindowExW(exStyle, "STATIC", nint.Zero, style, 0, 0, 10, 10, nint.Zero, nint.Zero, nint.Zero, nint.Zero);
        _windowReady = _hwnd != nint.Zero;

        if (!_windowReady)
        {
            Log.OverlayWindowCreateFailed(_logger);
        }
    }

    private void Render(string text, byte alpha)
    {
        var dpi = GetDpiForWindow(_hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var scale = dpi / 96f;
        var font = new Font("Microsoft YaHei UI", 9.5f * scale, FontStyle.Regular, GraphicsUnit.Point);
        var iconSize = (int)(20 * scale);
        var paddingH = (int)(20 * scale);
        var paddingV = (int)(12 * scale);
        var gap = (int)(10 * scale);
        var maxWidth = (int)(460 * scale);

        using var measureBitmap = new Bitmap(1, 1);
        using var measureGraphics = Graphics.FromImage(measureBitmap);
        var textSize = measureGraphics.MeasureString(text, font);
        var textWidth = (int)Math.Min(textSize.Width, maxWidth) + 2;
        var textHeight = (int)Math.Ceiling(textSize.Height);

        var width = paddingH + iconSize + gap + textWidth + paddingH;
        var height = Math.Max(paddingV + textHeight + paddingV, iconSize + paddingV * 2);

        using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            var rect = new RectangleF(0.5f, 0.5f, width - 1, height - 1);
            var radius = 10 * scale;
            using var backgroundPath = CreateRoundedRect(rect, radius);

            // 背景：ElementUI #303133 近似色，不透明度 0.92。
            using (var backgroundBrush = new SolidBrush(Color.FromArgb(236, 38, 38, 40)))
            {
                graphics.FillPath(backgroundBrush, backgroundPath);
            }

            // 细描边提升层次感。
            using (var borderPen = new Pen(Color.FromArgb(28, 255, 255, 255), 1f))
            {
                graphics.DrawPath(borderPen, backgroundPath);
            }

            // 绿色对勾图标（ElementUI success）。
            var iconX = paddingH;
            var iconY = (height - iconSize) / 2f;
            using (var circleBrush = new SolidBrush(Color.FromArgb(255, 103, 194, 58)))
            {
                graphics.FillEllipse(circleBrush, iconX, iconY, iconSize, iconSize);
            }

            using (var checkPen = new Pen(Color.White, 2.2f * scale))
            {
                checkPen.StartCap = LineCap.Round;
                checkPen.EndCap = LineCap.Round;
                var p1 = new PointF(iconX + iconSize * 0.26f, iconY + iconSize * 0.52f);
                var p2 = new PointF(iconX + iconSize * 0.44f, iconY + iconSize * 0.70f);
                var p3 = new PointF(iconX + iconSize * 0.76f, iconY + iconSize * 0.32f);
                graphics.DrawLines(checkPen, new[] { p1, p2, p3 });
            }

            // 文本（超宽截断）。
            using var textBrush = new SolidBrush(Color.FromArgb(245, 244, 244));
            var layout = new RectangleF(
                paddingH + iconSize + gap,
                (height - textHeight) / 2f,
                textWidth,
                textHeight);
            var format = new StringFormat(StringFormatFlags.NoWrap)
            {
                Trimming = StringTrimming.EllipsisCharacter,
                LineAlignment = StringAlignment.Center,
            };
            graphics.DrawString(text, font, textBrush, layout, format);
        }

        // 分层窗口提交（premultiplied alpha）。
        var screenDc = GetDC(nint.Zero);
        try
        {
            var memDc = CreateCompatibleDC(screenDc);
            try
            {
                var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                try
                {
                    var original = SelectObject(memDc, hBitmap);

                    var windowPos = default(NativePoint);
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

                    GC.KeepAlive(original);
                }
                finally
                {
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
        var screenWidth = GetSystemMetrics(SM_CXSCREEN);
        var screenHeight = GetSystemMetrics(SM_CYSCREEN);
        var offsetBottom = (int)(64 * scale);

        GetWindowRect(_hwnd, out var windowRect);
        var x = (screenWidth - (windowRect.Right - windowRect.Left)) / 2;
        var y = screenHeight - offsetBottom - (windowRect.Bottom - windowRect.Top);

        SetWindowPos(
            _hwnd, nint.Zero, x, y, 0, 0,
            SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private static GraphicsPath CreateRoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string BuildPreview(ClipboardItem item)
    {
        var preview = item.GetPreviewText();
        if (string.IsNullOrWhiteSpace(preview))
        {
            preview = "新内容";
        }

        preview = preview.ReplaceLineEndings(" ");
        return preview.Length <= MaxPreviewLength ? preview : preview[..MaxPreviewLength] + "…";
    }

    // ---- Win32 interop（仅本类使用） ----

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const int SW_HIDE = 0;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

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
    private static extern int GetSystemMetrics(int index);

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
        _showCts?.Dispose();
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
