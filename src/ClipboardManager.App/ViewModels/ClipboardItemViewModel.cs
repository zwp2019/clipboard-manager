using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ClipboardManager.App.ViewModels;

/// <summary>
/// 列表项 ViewModel：展示文本、类型图标、时间与缩略图（懒加载 + LRU 缓存，规范 §22、§26、§37）。
/// </summary>
public partial class ClipboardItemViewModel : ObservableObject
{
    private readonly LruImageCache _imageCache;

    public ClipboardItem Item { get; }

    [ObservableProperty]
    private BitmapImage? thumbnailSource;

    [ObservableProperty]
    private bool isFavorite;

    public ClipboardItemViewModel(ClipboardItem item, LruImageCache imageCache)
    {
        Item = item;
        _imageCache = imageCache;
        IsFavorite = item.IsFavorite;
    }

    public bool IsImage => Item.Type == ClipboardType.Image;

    public Visibility ThumbnailVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TypeIconVisibility => IsImage ? Visibility.Collapsed : Visibility.Visible;

    public string TypeGlyph => Item.Type switch
    {
        ClipboardType.Image => "\uEB9F",   // Photo
        ClipboardType.Url => "\uE774",     // Globe
        ClipboardType.File => "\uE8E5",    // OpenFile
        _ => "\uE8A5",                     // Document
    };

    public string DisplayText
    {
        get
        {
            if (Item.Type == ClipboardType.Image)
            {
                return "[图片]";
            }

            var preview = Item.GetPreviewText() ?? string.Empty;
            var oneLine = preview.Replace("\r", " ").Replace("\n", " ").Trim();
            return oneLine.Length == 0 ? "(空内容)" : oneLine;
        }
    }

    public string DetailText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Item.SourceApplication))
            {
                parts.Add(Item.SourceApplication);
            }

            if (Item.Type == ClipboardType.File && !string.IsNullOrEmpty(Item.FilePaths))
            {
                parts.Add("文件");
            }

            return parts.Count > 0 ? string.Join(" · ", parts) : string.Empty;
        }
    }

    public Visibility DetailTextVisibility =>
        string.IsNullOrEmpty(DetailText) ? Visibility.Collapsed : Visibility.Visible;

    public string TimeText
    {
        get
        {
            var diff = DateTime.Now - Item.CreatedAt;
            if (diff.TotalMinutes < 1)
            {
                return "刚刚";
            }

            if (diff.TotalHours < 1)
            {
                return $"{(int)diff.TotalMinutes} 分钟前";
            }

            if (Item.CreatedAt.Date == DateTime.Today)
            {
                return $"{(int)diff.TotalHours} 小时前";
            }

            if (Item.CreatedAt.Date == DateTime.Today.AddDays(-1))
            {
                return "昨天";
            }

            return Item.CreatedAt.ToString("MM-dd HH:mm");
        }
    }

    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734"; // FavoriteFill / Favorite

    /// <summary>
    /// 懒加载缩略图（在 UI 线程调用）：从 LRU 缓存读取，未命中时解码文件并写入缓存（规范 §22）。
    /// </summary>
    public async void EnsureThumbnail()
    {
        if (!IsImage || ThumbnailSource is not null)
        {
            return;
        }

        var path = Item.ThumbnailPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        var cached = _imageCache.Get(path);
        if (cached is not null)
        {
            ThumbnailSource = cached;
            return;
        }

        try
        {
            // WinUI BitmapImage 在文件可用时同步解码；大图异步解码由框架内部处理。
            var bitmap = new BitmapImage(new Uri(path));
            _imageCache.Put(path, bitmap);
            ThumbnailSource = bitmap;
        }
        catch (Exception)
        {
            // 图片损坏/无法解码时跳过（规范 §45）。
        }
    }

    public async Task ToggleFavoriteAsync(IHistoryService historyService)
    {
        IsFavorite = !IsFavorite;
        Item.IsFavorite = IsFavorite;
        OnPropertyChanged(nameof(FavoriteGlyph));
        await historyService.SetFavoriteAsync(Item.Id, IsFavorite, CancellationToken.None);
    }
}
