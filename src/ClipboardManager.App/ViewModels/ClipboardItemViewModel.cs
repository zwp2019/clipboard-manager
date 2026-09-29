using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace ClipboardManager.App.ViewModels;

/// <summary>
/// 列表项 ViewModel：展示文本、类型图标、时间与缩略图（懒加载 + LRU 缓存，规范 §22、§26、§37）。
/// 现在同时承担底部 Dock 风格卡片的展示数据。
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

    /// <summary>来源程序名称（exe 文件名去扩展名，如 "chrome"、"devenv"）。</summary>
    public string? SourceName => GetSourceName();

    private string? GetSourceName()
    {
        var path = Item.SourceApplication;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

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
            if (!string.IsNullOrEmpty(SourceName))
            {
                parts.Add(SourceName);
            }

            if (Item.Type == ClipboardType.File && !string.IsNullOrEmpty(Item.FilePaths))
            {
                parts.Add("文件");
            }

            return parts.Count > 0 ? string.Join(" · ", parts) : string.Empty;
        }
    }

    /// <summary>Dock 卡片顶部标题（中文）。</summary>
    public string CardTitle => Item.Type switch
    {
        ClipboardType.Code => "代码",
        ClipboardType.Url => "链接",
        ClipboardType.Image => "图片",
        ClipboardType.File => "文件",
        ClipboardType.Html => "HTML",
        ClipboardType.Rtf => "富文本",
        _ => "文本",
    };

    /// <summary>类型画笔资源键（便于 XAML 绑定到 Design Token）。</summary>
    public string TypeBrushKey => Item.Type switch
    {
        ClipboardType.Code => "TypeCodeBrush",
        ClipboardType.Url => "TypeUrlBrush",
        ClipboardType.Image => "TypeImageBrush",
        ClipboardType.File => "TypeFileBrush",
        ClipboardType.Html => "TypeHtmlBrush",
        ClipboardType.Rtf => "TypeRtfBrush",
        _ => "TypeTextBrush",
    };

    /// <summary>缩略图加载前的占位图标可见性（仅图片类型，避免文字卡片误显示图标）。</summary>
    public Visibility ThumbnailPlaceholderVisibility =>
        IsImage && ThumbnailSource is null ? Visibility.Visible : Visibility.Collapsed;

    partial void OnThumbnailSourceChanged(BitmapImage? value)
    {
        OnPropertyChanged(nameof(ThumbnailPlaceholderVisibility));
    }

    /// <summary>卡片底部信息（Paste 风格：来源 · 时间）。</summary>
    public string CardFooterText
    {
        get
        {
            var source = SourceName;
            return string.IsNullOrEmpty(source) ? TimeText : $"{source} · {TimeText}";
        }
    }

    /// <summary>Dock 卡片顶部背景色（统一使用 App.xaml 类型识别色 Design Token，静态缓存避免重复查资源字典）。</summary>
    private static readonly Dictionary<string, Brush> HeaderBrushCache = new();

    public Brush HeaderBrush
    {
        get
        {
            var key = TypeBrushKey;
            if (HeaderBrushCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var brush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
            HeaderBrushCache[key] = brush;
            return brush;
        }
    }

    /// <summary>卡片上展示的内容类型标签（Paste 风格）。</summary>
    public string TypeLabel => Item.Type switch
    {
        ClipboardType.Text => "文本",
        ClipboardType.Url => "链接",
        ClipboardType.Image => "图片",
        ClipboardType.File => "文件",
        ClipboardType.Html => "HTML",
        ClipboardType.Rtf => "RTF",
        ClipboardType.Code => "代码",
        _ => "其他",
    };

    /// <summary>卡片上展示的内容摘要（保留多行，截断至 300 字符避免长文本排版开销）。</summary>
    public string CardPreviewText
    {
        get
        {
            if (Item.Type == ClipboardType.Image)
            {
                return string.Empty;
            }

            if (Item.Type == ClipboardType.File)
            {
                return GetFilePreview();
            }

            var preview = Item.GetPreviewText() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(preview))
            {
                return "(空内容)";
            }

            preview = preview.Trim();
            return preview.Length > 300 ? preview[..300] : preview;
        }
    }

    public Visibility CardPreviewVisibility =>
        Item.Type == ClipboardType.Image ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>文件类型：显示数量与首路径文件名，避免暴露完整路径。</summary>
    private string GetFilePreview()
    {
        var paths = Item.FilePaths;
        if (string.IsNullOrWhiteSpace(paths))
        {
            return "文件";
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(paths);
            if (list is { Count: > 0 })
            {
                var firstName = Path.GetFileName(list[0]);
                return list.Count == 1 ? firstName : $"{firstName} 等 {list.Count} 项";
            }
        }
        catch (JsonException)
        {
            // 容错：解析失败退化为通用文案。
        }

        return "文件";
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

            return Item.CreatedAt.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
    }

    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734"; // FavoriteFill / Favorite

    // ---- 自定义分组（规范 §33） ----

    /// <summary>分组徽章文本。</summary>
    public string GroupBadgeText => Item.GroupName ?? string.Empty;

    public Visibility GroupBadgeVisibility =>
        string.IsNullOrEmpty(Item.GroupName) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>设置/移除分组：更新内存实体并落库，刷新徽章（UI 线程调用）。</summary>
    public async Task SetGroupAsync(IHistoryService historyService, string? group)
    {
        Item.GroupName = string.IsNullOrWhiteSpace(group) ? null : group.Trim();
        OnPropertyChanged(nameof(GroupBadgeText));
        OnPropertyChanged(nameof(GroupBadgeVisibility));
        await historyService.SetGroupAsync(Item.Id, Item.GroupName, CancellationToken.None);
    }

    // ---- 来源程序图标（Paste 风格：卡片底部显示来源应用小图标） ----

    /// <summary>进程级缓存：exe 路径 → 已解码图标（UI 线程专用 SoftwareBitmapSource）。</summary>
    private static readonly ConcurrentDictionary<string, SoftwareBitmapSource?> SourceIconCache = new();

    [ObservableProperty]
    private SoftwareBitmapSource? sourceAppIconSource;

    public Visibility SourceIconVisibility =>
        SourceAppIconSource is null ? Visibility.Collapsed : Visibility.Visible;

    partial void OnSourceAppIconSourceChanged(SoftwareBitmapSource? value)
    {
        OnPropertyChanged(nameof(SourceIconVisibility));
    }

    /// <summary>
    /// 懒加载来源程序图标（UI 线程调用）：后台提取 exe 关联图标并解码，回 UI 线程生成可绑定源。
    /// </summary>
    public async void EnsureSourceIcon()
    {
        var exePath = Item.SourceApplication;
        if (string.IsNullOrWhiteSpace(exePath) || SourceAppIconSource is not null)
        {
            return;
        }

        if (SourceIconCache.TryGetValue(exePath, out var cached))
        {
            SourceAppIconSource = cached;
            return;
        }

        try
        {
            // 后台：提取图标 → PNG 字节 → SoftwareBitmap（Bgra8 预乘）。
            var softwareBitmap = await Task.Run(() =>
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon is null)
                {
                    return null;
                }

                using var bitmap = icon.ToBitmap();
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;

                var decoder = BitmapDecoder.CreateAsync(ms.AsRandomAccessStream()).AsTask().GetAwaiter().GetResult();
                return decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied).AsTask().GetAwaiter().GetResult();
            });

            if (softwareBitmap is null)
            {
                SourceIconCache[exePath] = null;
                return;
            }

            // 回到 UI 线程创建 SoftwareBitmapSource。
            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(softwareBitmap);
            SourceIconCache[exePath] = source;
            SourceAppIconSource = source;
        }
        catch (Exception)
        {
            // 图标提取失败则缓存 null，避免重复尝试（规范 §45）。
            SourceIconCache[exePath] = null;
        }
    }

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
