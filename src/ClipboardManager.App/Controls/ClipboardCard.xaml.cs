using ClipboardManager.App.ViewModels;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace ClipboardManager.App.Controls;

public sealed partial class ClipboardCard : UserControl, IDisposable
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(ClipboardItemViewModel), typeof(ClipboardCard), new PropertyMetadata(null, OnItemChanged));
    private CancellationTokenSource? _loadCts;
    private SpriteVisual? _shadowVisual;
    private DropShadow? _shadow;
    private CompositionBrush? _shadowMask;
    private CompositionColorBrush? _shadowFill;

    public ClipboardItemViewModel? Item
    {
        get => (ClipboardItemViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }
    public ClipboardCard()
    {
        InitializeComponent();
        Loaded += (_, _) => { CreateShadow(); StartLoading(); };
        Unloaded += (_, _) => Dispose();
    }
    private static void OnItemChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    { if (sender is ClipboardCard { IsLoaded: true } card) card.StartLoading(); }
    private void CreateShadow()
    {
        if (_shadowVisual is not null || new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) return;
        try
        {
            var compositor = ElementCompositionPreview.GetElementVisual(ShadowHost).Compositor;
            _shadowMask = ShadowMask.GetAlphaMask();
            _shadow = compositor.CreateDropShadow();
            _shadow.Mask = _shadowMask;
            _shadow.BlurRadius = 12;
            _shadow.Opacity = 0.11f;
            _shadow.Offset = new Vector3(0, 3, 0);
            _shadow.Color = Microsoft.UI.Colors.Black;
            _shadowFill = compositor.CreateColorBrush(Microsoft.UI.Colors.Transparent);
            _shadowVisual = compositor.CreateSpriteVisual();
            _shadowVisual.Size = new Vector2(208, 208);
            _shadowVisual.Brush = _shadowFill;
            _shadowVisual.Shadow = _shadow;
            ElementCompositionPreview.SetElementChildVisual(ShadowHost, _shadowVisual);
        }
        catch (Exception ex)
        {
            ReleaseShadow();
            Serilog.Log.Debug(ex, "Card shadow unavailable");
        }
    }
    private void ReleaseShadow()
    {
        ElementCompositionPreview.SetElementChildVisual(ShadowHost, null);
        _shadowVisual?.Dispose(); _shadowVisual = null;
        _shadow?.Dispose(); _shadow = null;
        _shadowMask?.Dispose(); _shadowMask = null;
        _shadowFill?.Dispose(); _shadowFill = null;
    }
    private async void StartLoading()
    {
        _loadCts?.Cancel(); _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var item = Item;
        if (item is null) return;
        try { await Task.WhenAll(item.LoadThumbnailAsync(_loadCts.Token), item.LoadSourceIconAsync(_loadCts.Token)); }
        catch (OperationCanceledException) { /* Virtualized card scrolled out of view. */ }
    }
    public void Dispose()
    {
        ReleaseShadow();
        _loadCts?.Cancel(); _loadCts?.Dispose(); _loadCts = null;
        GC.SuppressFinalize(this);
    }
}
