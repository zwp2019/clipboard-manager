using ClipboardManager.Domain;
using Microsoft.UI.Xaml;

namespace ClipboardManager.App;

/// <summary>
/// 主题服务：System / Light / Dark（规范 §47）。
/// </summary>
public sealed class ThemeService
{
    private readonly List<Window> _windows = new();

    public void Track(Window window)
    {
        if (!_windows.Contains(window))
        {
            _windows.Add(window);
        }
    }

    public void Apply(AppTheme theme)
    {
        var elementTheme = theme switch
        {
            AppTheme.Dark => ElementTheme.Dark,
            AppTheme.Light => ElementTheme.Light,
            _ => ElementTheme.Default,
        };

        foreach (var window in _windows)
        {
            if (window.Content is FrameworkElement root)
            {
                root.RequestedTheme = elementTheme;
            }
        }
    }
}
