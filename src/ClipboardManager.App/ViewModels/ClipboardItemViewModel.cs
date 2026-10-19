using System.Globalization;
using System.Text.Json;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ClipboardManager.App.ViewModels;

public partial class ClipboardItemViewModel(ClipboardItem item, CardAssetLoader assets) : ObservableObject
{
    public ClipboardItem Item { get; } = item;
    [ObservableProperty] private BitmapImage? thumbnailSource;
    [ObservableProperty] private BitmapImage? sourceAppIconSource;
    [ObservableProperty] private bool isFavorite = item.IsFavorite;
    [ObservableProperty] private string imageCaption = "正在载入图片…";

    public bool IsImage => Item.Type == ClipboardType.Image;
    private Uri? Link => Item.Type == ClipboardType.Url && Uri.TryCreate(Item.Text?.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" ? uri : null;
    public bool IsColor => Item.Type == ClipboardType.Text && System.Text.RegularExpressions.Regex.IsMatch(Item.Text?.Trim() ?? "", "^#[0-9a-fA-F]{6}$");
    public string? SourceName => string.IsNullOrEmpty(Item.SourceApplication) ? null : Path.GetFileNameWithoutExtension(Item.SourceApplication);
    public string CardTitle => !string.IsNullOrWhiteSpace(Item.Title) ? Item.Title : TypeLabel;
    public string TypeLabel => IsColor ? "颜色" : Item.Type switch
    {
        ClipboardType.Image => "图片", ClipboardType.Url => "链接", ClipboardType.File => "文件",
        ClipboardType.Code => "代码", _ => "文本",
    };
    public string TypeGlyph => Item.Type switch
    {
        ClipboardType.Image => "\uEB9F", ClipboardType.Url => "\uE774", ClipboardType.File => "\uE8A5",
        ClipboardType.Code => "\uE943", _ => "\uE8A5",
    };
    public string TypeBrushKey => IsColor ? "TypeColorBrush" : Item.Type switch
    {
        ClipboardType.Image => "TypeImageBrush", ClipboardType.Url => "TypeUrlBrush",
        ClipboardType.File => "TypeFileBrush", ClipboardType.Code => "TypeCodeBrush", _ => "TypeTextBrush",
    };
    public Brush HeaderBrush => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[TypeBrushKey];
    public Brush HeaderTextBrush => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[TypeBrushKey == "TypeTextBrush" ? "CardWarmHeaderTextBrush" : "CardHeaderTextBrush"];
    public FontFamily PreviewFont => (FontFamily)Microsoft.UI.Xaml.Application.Current.Resources[Item.Type == ClipboardType.Code ? "FontFamilyMono" : "FontFamilyUi"];
    public string ContentTitle => Item.Title ?? (Link is not null ? Item.SourceTitle ?? Link.Host : Item.Type == ClipboardType.File ? FileSummary : "");
    public Visibility ContentTitleVisibility => !string.IsNullOrWhiteSpace(ContentTitle) ? Visibility.Visible : Visibility.Collapsed;
    public int PreviewMaxLines => ContentTitleVisibility == Visibility.Visible ? 6 : 7;
    public string CardPreviewText
    {
        get
        {
            if (IsImage) return "";
            var text = Item.Type == ClipboardType.File ? FileSummary : Item.GetPreviewText()?.Trim() ?? "";
            return text.Length > 500 ? text[..500] : text;
        }
    }
    public string CardFooterText => IsImage ? ImageCaption : Link is { } link
        ? link.Host + (link.AbsolutePath == "/" ? "" : link.PathAndQuery)
        : Item.Type == ClipboardType.File ? FileSummary : $"{new StringInfo(Item.Text ?? "").LengthInTextElements:N0} 个字符";
    public HorizontalAlignment FooterAlignment => Link is not null || Item.Type == ClipboardType.File ? HorizontalAlignment.Left : HorizontalAlignment.Center;
    public string SourceDescription => $"{SourceName ?? "本地"} · {Item.CreatedAt:yyyy-MM-dd HH:mm}";
    public Visibility ThumbnailVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CardPreviewVisibility => !IsImage && !IsColor ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ColorVisibility => IsColor ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ImagePlaceholderVisibility => IsImage && ThumbnailSource is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SourceIconVisibility => SourceAppIconSource is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SourceFallbackVisibility => SourceAppIconSource is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FavoriteVisibility => IsFavorite ? Visibility.Visible : Visibility.Collapsed;
    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";
    public string GroupBadgeText => Item.GroupName ?? "";
    public Visibility GroupBadgeVisibility => string.IsNullOrEmpty(Item.GroupName) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ThumbnailPlaceholderVisibility => ImagePlaceholderVisibility;
    public string? DisplayText => Item.GetPreviewText();

    private string FileSummary
    {
        get
        {
            try
            {
                var paths = JsonSerializer.Deserialize<string[]>(Item.FilePaths ?? "[]") ?? [];
                return paths.Length switch { 0 => "文件", 1 => Path.GetFileName(paths[0]), _ => $"{Path.GetFileName(paths[0])} 等 {paths.Length} 项" };
            }
            catch (JsonException) { return "文件"; }
        }
    }
    public Brush ColorBrush
    {
        get
        {
            var value = IsColor ? Convert.ToInt32(Item.Text!.Trim()[1..], 16) : 0;
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value));
        }
    }
    public Brush ColorTextBrush
    {
        get
        {
            var c = ((SolidColorBrush)ColorBrush).Color;
            return new SolidColorBrush(c.R * 0.299 + c.G * 0.587 + c.B * 0.114 > 160 ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White);
        }
    }
    public string TimeText
    {
        get
        {
            var elapsed = DateTime.Now - Item.CreatedAt;
            if (elapsed.TotalMinutes < 1) return "刚刚";
            if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes} 分钟前";
            if (Item.CreatedAt.Date == DateTime.Today) return $"{(int)elapsed.TotalHours} 小时前";
            if (Item.CreatedAt.Date == DateTime.Today.AddDays(-1)) return "昨天";
            return Item.CreatedAt.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
    }
    partial void OnThumbnailSourceChanged(BitmapImage? value)
    {
        OnPropertyChanged(nameof(ImagePlaceholderVisibility));
        OnPropertyChanged(nameof(ThumbnailPlaceholderVisibility));
    }
    partial void OnSourceAppIconSourceChanged(BitmapImage? value)
    {
        OnPropertyChanged(nameof(SourceIconVisibility)); OnPropertyChanged(nameof(SourceFallbackVisibility));
    }
    partial void OnImageCaptionChanged(string value) => OnPropertyChanged(nameof(CardFooterText));
    partial void OnIsFavoriteChanged(bool value)
    { OnPropertyChanged(nameof(FavoriteGlyph)); OnPropertyChanged(nameof(FavoriteVisibility)); }

    public async Task LoadThumbnailAsync(CancellationToken ct)
    {
        if (!IsImage || ThumbnailSource is not null) return;
        var result = await assets.LoadThumbnailAsync(Item, ct);
        ct.ThrowIfCancellationRequested();
        ThumbnailSource = result.Image; ImageCaption = result.Caption;
    }
    public async Task LoadSourceIconAsync(CancellationToken ct)
    {
        if (SourceAppIconSource is not null) return;
        var source = await assets.LoadIconAsync(Item.SourceApplication, ct);
        ct.ThrowIfCancellationRequested();
        SourceAppIconSource = source;
    }
    public async Task ToggleFavoriteAsync(IHistoryService history)
    { await history.SetFavoriteAsync(Item.Id, !IsFavorite, default); Item.IsFavorite = IsFavorite = !IsFavorite; }
    public async Task SetGroupAsync(IHistoryService history, string? group)
    {
        await history.SetGroupAsync(Item.Id, group, default); Item.GroupName = group;
        OnPropertyChanged(nameof(GroupBadgeText)); OnPropertyChanged(nameof(GroupBadgeVisibility));
    }
}
