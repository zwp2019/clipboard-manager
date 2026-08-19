using ClipboardManager.Domain;

namespace ClipboardManager.Contracts;

/// <summary>
/// 设置服务：强类型加载/保存（规范 §46）。
/// </summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    AppSettings Load();

    void Save(AppSettings settings);

    void Update(Action<AppSettings> mutate);
}

/// <summary>
/// 全局热键服务（规范 §28）。
/// </summary>
public interface IHotkeyService
{
    event EventHandler? HotkeyPressed;

    bool IsRegistered { get; }

    /// <summary>注册热键，格式如 "Ctrl+Shift+V"。</summary>
    bool Register(string hotkey);

    void Unregister();

    /// <summary>校验热键字符串是否合法。</summary>
    bool TryParse(string hotkey, out string? error);
}

/// <summary>
/// 快速粘贴服务（规范 §30）。
/// </summary>
public interface IQuickPasteService
{
    Task<bool> PasteAsync(ClipboardItem item, nint targetWindowHandle, CancellationToken ct);
}

/// <summary>
/// 窗口激活器：恢复目标窗口为前台窗口（规范 §30）。
/// </summary>
public interface IWindowActivator
{
    void ActivateWindow(nint windowHandle);
}

/// <summary>
/// 输入模拟器：向前台窗口发送粘贴按键（规范 §30）。
/// </summary>
public interface IInputSimulator
{
    void SendPaste();
}

/// <summary>
/// 系统托盘服务（规范 §31）。
/// </summary>
public interface ITrayService
{
    event EventHandler<TrayCommand>? CommandInvoked;

    void Start();

    void Stop();
}

public enum TrayCommand
{
    OpenClipboard,
    Settings,
    PauseMonitoring,
    ResumeMonitoring,
    ClearHistory,
    Quit,
}

/// <summary>
/// 开机自启服务（规范 §32）。
/// </summary>
public interface IAutoStartService
{
    bool IsEnabled();

    void Enable();

    void Disable();
}

/// <summary>
/// UI 线程调度抽象（便于测试，规范 §36）。
/// </summary>
public interface IDispatcher
{
    void Post(Action action);

    Task PostAsync(Action action);
}

/// <summary>
/// 防抖器（规范 §27）。
/// </summary>
public interface IDebouncer : IDisposable
{
    void Debounce(Action action, int delayMilliseconds);
}
