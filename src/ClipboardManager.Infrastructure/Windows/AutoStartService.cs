using ClipboardManager.Contracts;
using Microsoft.Win32;

namespace ClipboardManager.Infrastructure;

/// <summary>
/// 开机自启服务：写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run（规范 §32）。
/// 只在用户显式开启时写入，绝不默认修改。
/// </summary>
public sealed class AutoStartService : IAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipboardManager";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is not null;
    }

    public void Enable()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key?.SetValue(ValueName, $"\"{executablePath}\"");
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
