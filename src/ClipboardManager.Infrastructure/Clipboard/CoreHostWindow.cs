using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ClipboardManager.Contracts;
using ClipboardManager.Infrastructure.Interop;

namespace ClipboardManager.Infrastructure;

/// <summary>
/// 宿主消息窗口（message-only window）：承载剪贴板监听、全局热键与托盘消息（规范 §12、§28、§31）。
/// 在独立后台线程运行消息循环，禁止轮询。
/// 热键注册必须在窗口所属线程执行（error 1408: belongs to other thread），通过 PostMessage 派发。
/// </summary>
public sealed class CoreHostWindow : IDisposable
{
    private static readonly ConcurrentDictionary<nint, CoreHostWindow> s_windows = new();
    private static readonly NativeMethods.WndProcDelegate s_wndProc = WndProc;
    private static int s_windowCounter;

    private const uint WM_APP_REGISTER_HOTKEY = NativeMethods.WM_APP + 0x100;
    private const uint WM_APP_UNREGISTER_HOTKEY = NativeMethods.WM_APP + 0x101;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly string _className;
    private readonly object _lifecycle = new();

    private nint _hwnd;
    private bool _running;
    private bool _disposed;

    // 热键注册请求（线程间传递）
    private volatile int _pendingHotkeyId;
    private volatile uint _pendingModifiers;
    private volatile uint _pendingVirtualKey;
    private TaskCompletionSource<bool>? _hotkeyTcs;

    public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;
    public event EventHandler? HotkeyPressed;
    public event EventHandler<HostWindowMessageEventArgs>? MessageReceived;

    public CoreHostWindow()
    {
        _className = $"ClipboardManager_Host_{Interlocked.Increment(ref s_windowCounter)}";
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = _className,
        };
    }

    public nint Handle => _hwnd;

    /// <summary>
    /// 在宿主窗口线程注册全局热键（规范 §28）。
    /// RegisterHotKey 必须在窗口所属线程调用，否则返回 error 1408。
    /// </summary>
    public Task<bool> RegisterHotKeyOnHostThreadAsync(int id, uint modifiers, uint virtualKey)
    {
        if (_hwnd == nint.Zero)
        {
            return Task.FromResult(false);
        }

        _pendingHotkeyId = id;
        _pendingModifiers = modifiers;
        _pendingVirtualKey = virtualKey;
        _hotkeyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        NativeMethods.PostMessageW(_hwnd, WM_APP_REGISTER_HOTKEY, nint.Zero, nint.Zero);
        return _hotkeyTcs.Task;
    }

    /// <summary>
    /// 在宿主窗口线程取消全局热键。
    /// </summary>
    public Task UnregisterHotKeyOnHostThreadAsync(int id)
    {
        if (_hwnd == nint.Zero)
        {
            return Task.CompletedTask;
        }

        _pendingHotkeyId = id;
        _hotkeyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        NativeMethods.PostMessageW(_hwnd, WM_APP_UNREGISTER_HOTKEY, nint.Zero, nint.Zero);
        return _hotkeyTcs.Task;
    }

    public void Start()
    {
        lock (_lifecycle)
        {
            if (_running)
            {
                return;
            }

            _running = true;
            _thread.Start();
        }

        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Failed to create host window within 5 seconds.");
        }
    }

    public void Stop()
    {
        lock (_lifecycle)
        {
            _running = false;
        }

        if (_hwnd != nint.Zero)
        {
            NativeMethods.PostMessageW(_hwnd, NativeMethods.WM_DESTROY, nint.Zero, nint.Zero);
        }
    }

    private void ThreadMain()
    {
        var hInstance = NativeMethods.GetModuleHandleW(null);
        var wc = new NativeMethods.WNDCLASSW
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = nint.Zero,
            hCursor = nint.Zero,
            hbrBackground = nint.Zero,
            lpszMenuName = null,
            lpszClassName = _className,
        };

        if (NativeMethods.RegisterClassW(ref wc) == 0)
        {
            _ready.Set();
            return;
        }

        _hwnd = NativeMethods.CreateWindowExW(
            0, _className, "ClipboardManager", 0,
            0, 0, 0, 0,
            new nint(-3), // HWND_MESSAGE：仅用于消息传递
            nint.Zero, hInstance, nint.Zero);

        if (_hwnd == nint.Zero)
        {
            NativeMethods.UnregisterClassW(_className, hInstance);
            _ready.Set();
            return;
        }

        s_windows[_hwnd] = this;
        _ready.Set();

        try
        {
            while (true)
            {
                bool running;
                lock (_lifecycle)
                {
                    running = _running;
                }

                if (!running)
                {
                    break;
                }

                if (!NativeMethods.GetMessageW(out var msg, nint.Zero, 0, 0))
                {
                    break;
                }

                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessageW(ref msg);
            }
        }
        finally
        {
            s_windows.TryRemove(_hwnd, out _);
            if (_hwnd != nint.Zero)
            {
                NativeMethods.DestroyWindow(_hwnd);
                _hwnd = nint.Zero;
            }

            NativeMethods.UnregisterClassW(_className, hInstance);
        }
    }

    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (s_windows.TryGetValue(hwnd, out var window))
        {
            switch (msg)
            {
                case NativeMethods.WM_CLIPBOARDUPDATE:
                    window.ClipboardChanged?.Invoke(window, new ClipboardChangedEventArgs
                    {
                        OperationId = Guid.NewGuid().ToString("N"),
                    });
                    return 0;

                case NativeMethods.WM_HOTKEY:
                    window.HotkeyPressed?.Invoke(window, EventArgs.Empty);
                    return 0;

                case WM_APP_REGISTER_HOTKEY:
                    window.HandleRegisterHotkey(hwnd);
                    return 0;

                case WM_APP_UNREGISTER_HOTKEY:
                    window.HandleUnregisterHotkey(hwnd);
                    return 0;

                case NativeMethods.WM_COMMAND:
                case NativeMethods.WM_TRAYICON:
                    window.MessageReceived?.Invoke(window, new HostWindowMessageEventArgs(msg, wParam, lParam));
                    return 0;
            }
        }

        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void HandleRegisterHotkey(nint hwnd)
    {
        var ok = NativeMethods.RegisterHotKey(hwnd, _pendingHotkeyId, _pendingModifiers, _pendingVirtualKey);
        _hotkeyTcs?.TrySetResult(ok);
        _hotkeyTcs = null;
    }

    private void HandleUnregisterHotkey(nint hwnd)
    {
        NativeMethods.UnregisterHotKey(hwnd, _pendingHotkeyId);
        _hotkeyTcs?.TrySetResult(true);
        _hotkeyTcs = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _ready.Dispose();
    }
}

/// <summary>
/// 宿主窗口收到的通用消息（托盘、菜单等）。
/// </summary>
public sealed class HostWindowMessageEventArgs : EventArgs
{
    public uint Message { get; }

    public nint WParam { get; }

    public nint LParam { get; }

    public HostWindowMessageEventArgs(uint message, nint wParam, nint lParam)
    {
        Message = message;
        WParam = wParam;
        LParam = lParam;
    }
}
