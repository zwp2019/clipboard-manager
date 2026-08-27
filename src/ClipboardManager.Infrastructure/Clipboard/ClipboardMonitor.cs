using System.Runtime.InteropServices;
using ClipboardManager.Contracts;
using ClipboardManager.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Infrastructure;

/// <summary>
/// 剪贴板监听器：基于 Windows 原生事件（WM_CLIPBOARDUPDATE），事件驱动、零轮询（规范 §12）。
/// 自身写入剪贴板产生的事件会被过滤，防止死循环（规范 §13）。
/// </summary>
public sealed class ClipboardMonitor : IClipboardMonitor, IDisposable
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
            _logger.LogError("AddClipboardFormatListener failed: {Code}", Marshal.GetLastPInvokeError());
            throw new InvalidOperationException("Failed to register clipboard format listener.");
        }

        _started = true;
        _logger.LogDebug("Clipboard monitor started");
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
        _logger.LogDebug("Clipboard monitor stopped");
    }

    private void OnHostClipboardChanged(object? sender, ClipboardChangedEventArgs e)
    {
        // 自身写入期间产生的事件直接丢弃（规范 §13）。
        if (_guard.IsOwnWrite)
        {
            _logger.LogTrace("Clipboard change from own write ignored");
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
}
