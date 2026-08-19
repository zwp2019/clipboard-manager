using System.Runtime.InteropServices;
using ClipboardManager.Contracts;
using ClipboardManager.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Infrastructure;

/// <summary>
/// 全局热键服务：RegisterHotKey 原生实现，支持注册失败检测（规范 §28）。
/// </summary>
public sealed class GlobalHotkeyService : IHotkeyService
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
            _logger.LogWarning("Invalid hotkey {Hotkey}: {Error}", hotkey, error);
            return false;
        }

        if (!NativeMethods.RegisterHotKey(_hostWindow.Handle, HotkeyId, modifiers, virtualKey))
        {
            // 注册失败：可能与其他应用冲突（规范 §28 冲突检测）。
            _logger.LogWarning(
                "RegisterHotKey failed for {Hotkey}, error={Code}",
                hotkey,
                Marshal.GetLastPInvokeError());
            return false;
        }

        _hostWindow.HotkeyPressed += OnHotkeyPressed;
        IsRegistered = true;
        _logger.LogInformation("Global hotkey registered: {Hotkey}", hotkey);
        return true;
    }

    public void Unregister()
    {
        if (!IsRegistered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_hostWindow.Handle, HotkeyId);
        _hostWindow.HotkeyPressed -= OnHotkeyPressed;
        IsRegistered = false;
    }

    public bool TryParse(string hotkey, out string? error)
        => HotkeyParser.TryParse(hotkey, out _, out _, out error);

    private void OnHotkeyPressed(object? sender, EventArgs e)
        => HotkeyPressed?.Invoke(this, EventArgs.Empty);
}
