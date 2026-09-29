using System.Runtime.InteropServices;
using ClipboardManager.Contracts;
using ClipboardManager.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Infrastructure;

/// <summary>
/// 全局热键服务：RegisterHotKey 原生实现，支持注册失败检测（规范 §28）。
/// </summary>
public sealed partial class GlobalHotkeyService : IHotkeyService
{
    private const int HotkeyId = 1001;

    private readonly CoreHostWindow _hostWindow;
    private readonly ILogger<GlobalHotkeyService> _logger;

    public event EventHandler? HotkeyPressed;

    public bool IsRegistered { get; private set; }

    public GlobalHotkeyService(CoreHostWindow hostWindow, ILogger<GlobalHotkeyService> logger)
    {
        _hostWindow = hostWindow;
        _logger = logger;
    }

    public bool Register(string hotkey)
    {
        Unregister();
        _hostWindow.Start();

        if (!HotkeyParser.TryParse(hotkey, out var modifiers, out var virtualKey, out var error))
        {
            Log.InvalidHotkey(_logger, hotkey, error);
            return false;
        }

        // RegisterHotKey 必须在窗口所属线程调用（error 1408: belongs to other thread）。
        var ok = _hostWindow.RegisterHotKeyOnHostThreadAsync(HotkeyId, modifiers, virtualKey).GetAwaiter().GetResult();
        if (!ok)
        {
            // 注册失败：可能与其他应用冲突（规范 §28 冲突检测）。
            Log.RegisterHotkeyFailed(_logger, hotkey, Marshal.GetLastPInvokeError());
            return false;
        }

        _hostWindow.HotkeyPressed += OnHotkeyPressed;
        IsRegistered = true;
        Log.HotkeyRegistered(_logger, hotkey);
        return true;
    }

    public void Unregister()
    {
        if (!IsRegistered)
        {
            return;
        }

        _hostWindow.HotkeyPressed -= OnHotkeyPressed;
        _hostWindow.UnregisterHotKeyOnHostThreadAsync(HotkeyId).GetAwaiter().GetResult();
        IsRegistered = false;
    }

    public bool TryParse(string hotkey, out string? error)
        => HotkeyParser.TryParse(hotkey, out _, out _, out error);

    private void OnHotkeyPressed(object? sender, EventArgs e)
        => HotkeyPressed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 源生成日志方法，避免 CA1848/CA1873。
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(EventId = 0, Level = LogLevel.Warning, Message = "Invalid hotkey {Hotkey}: {Error}")]
        public static partial void InvalidHotkey(ILogger logger, string hotkey, string? error);

        [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "RegisterHotKey failed for {Hotkey}, error={Code}")]
        public static partial void RegisterHotkeyFailed(ILogger logger, string hotkey, int code);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Global hotkey registered: {Hotkey}")]
        public static partial void HotkeyRegistered(ILogger logger, string hotkey);
    }
}
