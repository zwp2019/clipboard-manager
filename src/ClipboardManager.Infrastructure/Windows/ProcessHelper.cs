using System.Runtime.InteropServices;
using ClipboardManager.Contracts;
using ClipboardManager.Infrastructure.Interop;

namespace ClipboardManager.Infrastructure;

/// <summary>
/// 进程/窗口辅助：前台窗口、进程名、窗口激活与粘贴按键模拟（规范 §30）。
/// </summary>
public static class ProcessHelper
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public static nint GetForegroundWindow()
        => NativeMethods.GetForegroundWindow();

    public static string? GetWindowTitle(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            return null;
        }

        var length = NativeMethods.GetWindowTextLengthW(windowHandle);
        if (length <= 0)
        {
            return null;
        }

        var buffer = new char[length + 1];
        var copied = NativeMethods.GetWindowTextW(windowHandle, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : null;
    }

    /// <summary>
    /// 获取窗口所属进程的可执行文件名（不含扩展名）。用于来源应用记录与敏感应用排除（规范 §39）。
    /// </summary>
    public static string? GetProcessName(nint windowHandle)
    {
        var path = GetProcessImagePath(windowHandle);
        return path is null ? null : Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>
    /// 获取窗口所属进程的可执行文件完整路径。用于来源应用图标提取（Icon.ExtractAssociatedIcon）。
    /// </summary>
    public static string? GetProcessImagePath(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            return null;
        }

        if (NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId) == 0 || processId == 0)
        {
            return null;
        }

        var process = NativeMethods.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == nint.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            return NativeMethods.QueryFullProcessImageNameW(process, 0, buffer, ref size)
                ? new string(buffer, 0, (int)size)
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    public static void ActivateWindow(nint windowHandle)
    {
        if (windowHandle == nint.Zero || !NativeMethods.IsWindow(windowHandle))
        {
            return;
        }

        NativeMethods.ShowWindow(windowHandle, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(windowHandle);
    }

    /// <summary>
    /// 向前台窗口发送 Ctrl+V 粘贴（SendInput，规范 §30）。
    /// 注意：目标窗口若以更高权限（管理员/UAC）运行，普通权限进程无法注入输入（规范 §30 已声明考虑项）。
    /// </summary>
    public static void SendPaste()
    {
        var ctrlDown = CreateKeyInput(NativeMethods.VK_CONTROL, false);
        var vDown = CreateKeyInput(NativeMethods.VK_V, false);
        var vUp = CreateKeyInput(NativeMethods.VK_V, true);
        var ctrlUp = CreateKeyInput(NativeMethods.VK_CONTROL, true);

        var inputs = new[] { ctrlDown, vDown, vUp, ctrlUp };

        // 返回值忽略：注入失败（如目标窗口权限更高）不阻断主流程（规范 §30）。
        _ = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT CreateKeyInput(ushort virtualKey, bool keyUp)
        => new()
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
                },
            },
        };
}

/// <summary>
/// 窗口激活器实现。
/// </summary>
public sealed class WindowActivator : IWindowActivator
{
    public void ActivateWindow(nint windowHandle)
        => ProcessHelper.ActivateWindow(windowHandle);
}

/// <summary>
/// 输入模拟器实现。
/// </summary>
public sealed class InputSimulator : IInputSimulator
{
    public void SendPaste()
        => ProcessHelper.SendPaste();
}
