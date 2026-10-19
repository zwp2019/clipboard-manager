using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;
using WinRT;

namespace ClipboardManager.App.Services;

/// <summary>Native desktop acrylic for floating dialogs and settings, with a solid
/// fallback. No screen capture, custom shader, animation timer or web runtime.</summary>
internal sealed class GlassWindowBackdrop : IDisposable
{
    private readonly Window _window;
    private readonly Grid _root;
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;

    public GlassWindowBackdrop(Window window, Grid root)
    {
        _window = window; _root = root;
        try
        {
            if (DesktopAcrylicController.IsSupported() && !new AccessibilitySettings().HighContrast)
            {
                window.DispatcherQueue.EnsureSystemDispatcherQueue();
                _configuration = new SystemBackdropConfiguration { IsInputActive = true };
                _controller = new DesktopAcrylicController();
                if (!_controller.AddSystemBackdropTarget(window.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>()))
                    throw new InvalidOperationException("Unable to attach the floating window acrylic backdrop.");
                _controller.SetSystemBackdropConfiguration(_configuration);
                root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }
        catch (Exception ex)
        {
            _controller?.Dispose(); _controller = null;
            Serilog.Log.Warning(ex, "Desktop acrylic unavailable; using the solid palette");
        }
        root.ActualThemeChanged += OnThemeChanged;
        window.Activated += OnActivated;
        UpdateTheme();
    }
    private void OnThemeChanged(FrameworkElement sender, object args) => UpdateTheme();
    private void OnActivated(object sender, WindowActivatedEventArgs args)
    { if (_configuration is not null) _configuration.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated; }
    private void UpdateTheme()
    {
        if (_controller is null) { _root.Background = GlassTheme.Brush(_root, "BackgroundBrush"); return; }
        var light = _root.ActualTheme == ElementTheme.Light;
        _controller.TintColor = light ? Windows.UI.Color.FromArgb(255, 237, 243, 255) : Windows.UI.Color.FromArgb(255, 35, 43, 61);
        _controller.TintOpacity = light ? 0.9f : 0.92f;
        _controller.LuminosityOpacity = light ? 0.9f : 0.2f;
        if (_configuration is not null) _configuration.Theme = light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;
    }
    public void Dispose()
    {
        _root.ActualThemeChanged -= OnThemeChanged;
        _window.Activated -= OnActivated;
        _controller?.Dispose(); _controller = null;
    }
}

internal static class GlassTheme
{
    internal static Brush Brush(FrameworkElement root, string key)
    {
        var theme = new AccessibilitySettings().HighContrast ? "HighContrast" : root.ActualTheme == ElementTheme.Light ? "Light" : "Default";
        return Find(Microsoft.UI.Xaml.Application.Current.Resources, theme, key)
            ?? throw new InvalidOperationException($"Missing glass theme resource: {key}");
    }
    private static Brush? Find(ResourceDictionary dictionary, string theme, string key)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(theme, out var raw) && raw is ResourceDictionary values && values.TryGetValue(key, out var value) && value is Brush brush) return brush;
        if (dictionary.TryGetValue(key, out var own) && own is Brush ownBrush) return ownBrush;
        foreach (var merged in dictionary.MergedDictionaries.Reverse())
        { var match = Find(merged, theme, key); if (match is not null) return match; }
        return null;
    }
}
