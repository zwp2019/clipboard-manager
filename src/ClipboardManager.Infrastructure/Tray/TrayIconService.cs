using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using ClipboardManager.Contracts;
using ClipboardManager.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Infrastructure;

/// <summary>
/// 系统托盘服务：Shell_NotifyIcon 原生实现（规范 §31）。
/// 图标运行时绘制（GDI+），避免依赖二进制图标资源。
/// </summary>
public sealed class TrayIconService : ITrayService, IDisposable
{
    private readonly CoreHostWindow _hostWindow;
    private readonly ILogger<TrayIconService> _logger;
    private readonly object _gate = new();

    private NativeMethods.NOTIFYICONDATAW _nid;
    private nint _hIcon;
    private bool _running;
    private bool _paused;

    public event EventHandler<TrayCommand>? CommandInvoked;

    public TrayIconService(CoreHostWindow hostWindow, ILogger<TrayIconService> logger)
    {
        _hostWindow = hostWindow;
        _logger = logger;
    }

    /// <summary>
    /// 更新暂停状态，影响右键菜单文案（Pause/Resume）。
    /// </summary>
    public void SetPaused(bool paused)
    {
        _paused = paused;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running)
            {
                return;
            }

            _hostWindow.Start();
            _hostWindow.MessageReceived += OnHostMessage;

            _hIcon = CreateTrayIcon();
            _nid = new NativeMethods.NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
                hWnd = _hostWindow.Handle,
                uID = 1,
                uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
                uCallbackMessage = NativeMethods.WM_TRAYICON,
                hIcon = _hIcon,
                szTip = "剪贴板管理器",
            };

            if (!NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_ADD, ref _nid))
            {
                _logger.LogWarning("Shell_NotifyIcon add failed, error={Code}", Marshal.GetLastPInvokeError());
            }

            _running = true;
            _logger.LogDebug("Tray icon started");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_DELETE, ref _nid);
            _hostWindow.MessageReceived -= OnHostMessage;

            if (_hIcon != nint.Zero)
            {
                NativeMethods.DeleteObject(_hIcon);
                _hIcon = nint.Zero;
            }

            _running = false;
            _logger.LogDebug("Tray icon stopped");
        }
    }

    private void OnHostMessage(object? sender, HostWindowMessageEventArgs e)
    {
        if (e.Message == NativeMethods.WM_TRAYICON)
        {
            var mouseMessage = (uint)(e.LParam.ToInt64() & 0xFFFF);
            switch (mouseMessage)
            {
                case NativeMethods.WM_LBUTTONUP:
                    CommandInvoked?.Invoke(this, TrayCommand.OpenClipboard);
                    break;

                case NativeMethods.WM_RBUTTONUP:
                    ShowContextMenu();
                    break;
            }
        }
        else if (e.Message == NativeMethods.WM_COMMAND)
        {
            var commandId = e.WParam.ToInt32() & 0xFFFF;
            if (Enum.IsDefined(typeof(TrayCommand), commandId))
            {
                CommandInvoked?.Invoke(this, (TrayCommand)commandId);
            }
        }
    }

    private void ShowContextMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == nint.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING, (nuint)TrayCommand.OpenClipboard, "打开剪贴板");
            NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING, (nuint)TrayCommand.Settings, "设置");
            NativeMethods.AppendMenuW(menu, NativeMethods.MF_SEPARATOR, 0, null);

            if (_paused)
            {
                NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING, (nuint)TrayCommand.ResumeMonitoring, "恢复监听");
            }
            else
            {
                NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING, (nuint)TrayCommand.PauseMonitoring, "暂停监听");
            }

            NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING, (nuint)TrayCommand.ClearHistory, "清空历史");
            NativeMethods.AppendMenuW(menu, NativeMethods.MF_SEPARATOR, 0, null);
            NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING, (nuint)TrayCommand.Quit, "退出");

            NativeMethods.GetCursorPos(out var point);
            NativeMethods.SetForegroundWindow(_hostWindow.Handle);
            NativeMethods.TrackPopupMenu(menu, NativeMethods.TPM_RIGHTBUTTON, point.X, point.Y, 0, _hostWindow.Handle, nint.Zero);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private static nint CreateTrayIcon()
    {
        // 优先加载应用图标 Assets/app.ico（与 exe / 任务栏图标一致），失败回退运行时绘制。
        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(icoPath))
            {
                using var icon = new Icon(icoPath, 32, 32);
                using var icoBitmap = icon.ToBitmap();
                return icoBitmap.GetHicon();
            }
        }
        catch
        {
            // 回退到 GDI+ 绘制。
        }

        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var brush = new SolidBrush(Color.White);

            // 剪贴板夹子
            graphics.FillRectangle(brush, 12, 3, 8, 6);

            // 剪贴板面板（圆角矩形）
            var path = new GraphicsPath();
            var rect = new Rectangle(7, 8, 18, 21);
            var radius = 4;
            path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
            path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
            path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            graphics.FillPath(brush, path);

            // 文档线条
            using var pen = new Pen(Color.Transparent, 1f);
            graphics.DrawLine(pen, 11, 16, 21, 16);
            graphics.DrawLine(pen, 11, 20, 21, 20);
            graphics.DrawLine(pen, 11, 24, 18, 24);
        }

        return bitmap.GetHicon();
    }

    public void Dispose()
    {
        Stop();
    }
}
