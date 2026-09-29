using System.Runtime.InteropServices;
using ClipboardManager.Contracts;
using ClipboardManager.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Infrastructure;

/// <summary>
/// 剪贴板监听器：基于 Windows 原生事件（WM_CLIPBOARDUPDATE），事件驱动、零轮询（规范 §12）。
/// 自身写入剪贴板产生的事件会被过滤，防止死循环（规范 §13）。
/// </summary>
public sealed partial class ClipboardMonitor : IClipboardMonitor, IDisposable
{
    private readonly CoreHostWindow _hostWindow;
    private readonly IInternalClipboardGuard _guard;
    private readonly ILogger<ClipboardMonitor> _logger;

    private bool _started;

    public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;

    public bool IsRunning => _started;

    public ClipboardMonitor(
        CoreHostWindow hostWindow,
        IInternalClipboardGuard guard,
        ILogger<ClipboardMonitor> logger)
    {
        _hostWindow = hostWindow;
        _guard = guard;
        _logger = logger;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _hostWindow.Start();
        _hostWindow.ClipboardChanged += OnHostClipboardChanged;

        if (!NativeMethods.AddClipboardFormatListener(_hostWindow.Handle))
        {
            Log.AddListenerFailed(_logger, Marshal.GetLastPInvokeError());
            throw new InvalidOperationException("Failed to register clipboard format listener.");
        }

        _started = true;
        Log.MonitorStarted(_logger);
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _hostWindow.ClipboardChanged -= OnHostClipboardChanged;
        if (_hostWindow.Handle != nint.Zero)
        {
            NativeMethods.RemoveClipboardFormatListener(_hostWindow.Handle);
        }

        _started = false;
        Log.MonitorStopped(_logger);
    }

    private void OnHostClipboardChanged(object? sender, ClipboardChangedEventArgs e)
    {
        // 自身写入期间产生的事件直接丢弃（规范 §13）。
        if (_guard.IsOwnWrite)
        {
            Log.OwnWriteIgnored(_logger);
            return;
        }

        // 在事件触发的瞬间捕获前台窗口进程（复制来源），
        // 延迟到读取线程时前台窗口可能已被用户切换。
        e.SourceApplication = ProcessHelper.GetProcessImagePath(ProcessHelper.GetForegroundWindow());

        ClipboardChanged?.Invoke(this, e);
    }

    public void Dispose()
    {
        Stop();
    }

    /// <summary>
    /// 源生成日志方法，避免 CA1848/CA1873。
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(EventId = 0, Level = LogLevel.Error, Message = "AddClipboardFormatListener failed: {Code}")]
        public static partial void AddListenerFailed(ILogger logger, int code);

        [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Clipboard monitor started")]
        public static partial void MonitorStarted(ILogger logger);

        [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Clipboard monitor stopped")]
        public static partial void MonitorStopped(ILogger logger);

        [LoggerMessage(EventId = 3, Level = LogLevel.Trace, Message = "Clipboard change from own write ignored")]
        public static partial void OwnWriteIgnored(ILogger logger);
    }
}
