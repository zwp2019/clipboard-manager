using ClipboardManager.Infrastructure.Interop;

namespace ClipboardManager.Infrastructure;

/// <summary>
/// 热键解析器：将 "Ctrl+Shift+V" 形式转换为 Win32 修饰键与虚拟键（规范 §28）。
/// </summary>
public static class HotkeyParser
{
    public static bool TryParse(string hotkey, out uint modifiers, out uint virtualKey, out string? error)
    {
        modifiers = 0;
        virtualKey = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(hotkey))
        {
            error = "热键不能为空";
            return false;
        }

        var parts = hotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            error = "热键必须包含至少一个修饰键（如 Ctrl+Shift+V）";
            return false;
        }

        var keyFound = false;
        foreach (var rawPart in parts)
        {
            var part = rawPart.ToLowerInvariant();
            switch (part)
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "win":
                case "windows":
                case "super":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    if (TryGetVirtualKey(rawPart, out var vk))
                    {
                        virtualKey = vk;
                        keyFound = true;
                    }
                    else
                    {
                        error = $"无法识别的按键：{rawPart}";
                        return false;
                    }

                    break;
            }
        }

        if (!keyFound)
        {
            error = "缺少主按键（如 V）";
            return false;
        }

        return true;
    }

    private static bool TryGetVirtualKey(string name, out uint virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (name.Length == 1 && char.IsLetterOrDigit(name[0]))
        {
            var upper = char.ToUpperInvariant(name[0]);
            if (upper is >= 'A' and <= 'Z')
            {
                virtualKey = upper;
                return true;
            }

            if (upper is >= '0' and <= '9')
            {
                virtualKey = upper;
                return true;
            }
        }

        switch (name.ToLowerInvariant())
        {
            case "space":
                virtualKey = 0x20;
                return true;
            case "enter":
            case "return":
                virtualKey = 0x0D;
                return true;
            case "tab":
                virtualKey = 0x09;
                return true;
            case "esc":
            case "escape":
                virtualKey = 0x1B;
                return true;
            case "backspace":
                virtualKey = 0x08;
                return true;
            case "delete":
            case "del":
                virtualKey = 0x2E;
                return true;
            case "insert":
                virtualKey = 0x2D;
                return true;
            case "home":
                virtualKey = 0x24;
                return true;
            case "end":
                virtualKey = 0x23;
                return true;
            case "pageup":
                virtualKey = 0x21;
                return true;
            case "pagedown":
                virtualKey = 0x22;
                return true;
            default:
                if (name.StartsWith("f", StringComparison.OrdinalIgnoreCase)
                    && byte.TryParse(name.AsSpan(1), out var f)
                    && f is >= 1 and <= 24)
                {
                    virtualKey = (uint)(0x70 + f - 1);
                    return true;
                }

                return false;
        }
    }
}
