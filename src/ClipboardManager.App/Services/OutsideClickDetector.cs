using System.Runtime.InteropServices;
using ClipboardManager.Infrastructure.Interop;

namespace ClipboardManager.App.Services;

/// <summary>
/// 全局低级鼠标钩子：Launcher 显示期间监听鼠标按下，
/// 点击窗口矩形之外的区域时触发关闭（Paste 风格 launcher 行为，规范 §29）。
/// 只监听按下事件（拖拽释放不会误关），窗口内点击直接放行。
/// </summary>
internal sealed class OutsideClickDetector
{
    private readonly NativeMethods.LowLevelMouseProc _proc;
    private readonly Action _onOutsideClick;

    private nint _hwnd;
    private nint _hookId;

    public OutsideClickDetector(Action onOutsideClick)
    {
        _onOutsideClick = onOutsideClick;
        // 委托赋值给字段，防止 GC 回收后钩子回调崩溃。
        _proc = HookCallback;
    }

    /// <summary>安装钩子（重复调用幂等）。必须在有消息循环的 UI 线程调用。</summary>
    public void Start(nint hwnd)
    {
        if (_hookId != nint.Zero)
        {
            return;
        }

        _hwnd = hwnd;
        // WH_MOUSE_LL 为全局钩子：hMod 传本模块句柄、dwThreadId 传 0，回调运行在安装线程上下文。
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _proc,
            NativeMethods.GetModuleHandleW(null),
            0);
    }

    public void Stop()
    {
        if (_hookId == nint.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hookId);
        _hookId = nint.Zero;
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && IsMouseDown(wParam))
        {
            var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            if (!IsInsideWindow(info.pt))
            {
                _onOutsideClick();
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsMouseDown(nint wParam)
        => wParam == NativeMethods.WM_LBUTTONDOWN
            || wParam == NativeMethods.WM_RBUTTONDOWN
            || wParam == NativeMethods.WM_MBUTTONDOWN;

    private bool IsInsideWindow(NativeMethods.POINT point)
    {
        var rect = new NativeMethods.RECT();
        return NativeMethods.GetWindowRect(_hwnd, ref rect)
            && point.X >= rect.Left && point.X < rect.Right
            && point.Y >= rect.Top && point.Y < rect.Bottom;
    }
}
