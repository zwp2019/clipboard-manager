using System.Runtime.InteropServices.WindowsRuntime;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace ClipboardManager.App;

/// <summary>Decode local files from streams; file:// URI loading is unreliable for
/// unpackaged WinUI apps. Only thumbnails are decoded for the card strip.</summary>
public sealed partial class CardAssetLoader(LruImageCache cache, IImageStorageService images, ILogger<CardAssetLoader> logger)
{
    public async Task<(BitmapImage? Image, string Caption)> LoadThumbnailAsync(ClipboardItem item, CancellationToken ct)
    {
        try
        {
            var path = item.ThumbnailPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                path = await images.GenerateThumbnailAsync(item.ImagePath ?? "", ct);
                item.ThumbnailPath = path;
            }
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return (null, "图片文件已不存在");
            var caption = "图片";
            uint originalMaxSide = 0;
            if (File.Exists(item.ImagePath))
            {
                try
                {
                    var size = await ReadDimensionsAsync(item.ImagePath, ct);
                    originalMaxSide = Math.Max(size.Width, size.Height);
                    caption = $"{size.Width} × {size.Height}";
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { ThumbnailFailed(logger, ex, item.Id); }
            }
            var cached = cache.Get(path);
            if (cached is not null) return (cached, caption);
            BitmapImage bitmap;
            try
            {
                var size = await ReadDimensionsAsync(path, ct);
                if (Math.Max(size.Width, size.Height) < Math.Min(originalMaxSide, 448))
                    path = await images.GenerateThumbnailAsync(item.ImagePath ?? "", ct) ?? path;
                bitmap = await DecodeThumbnailAsync(path, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && File.Exists(item.ImagePath))
            {
                ThumbnailFailed(logger, ex, item.Id);
                path = await images.GenerateThumbnailAsync(item.ImagePath!, ct);
                if (path is null) return (null, "图片无法预览 · 空格查看详情");
                bitmap = await DecodeThumbnailAsync(path, ct);
            }
            item.ThumbnailPath = path;
            cache.Put(path, bitmap);
            return (bitmap, caption);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            ThumbnailFailed(logger, ex, item.Id);
            return (null, "图片无法预览 · 空格查看详情");
        }
    }

    private static async Task<(uint Width, uint Height)> ReadDimensionsAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream()).AsTask(ct);
        return (decoder.PixelWidth, decoder.PixelHeight);
    }
    private static async Task<BitmapImage> DecodeThumbnailAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
        var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream()).AsTask(ct);
        var bitmap = new BitmapImage { DecodePixelWidth = (int)Math.Min(448, decoder.PixelWidth) };
        stream.Position = 0;
        await bitmap.SetSourceAsync(stream.AsRandomAccessStream()).AsTask(ct);
        return bitmap;
    }

    public async Task<BitmapImage?> LoadIconAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var key = "icon:" + path;
        var cached = cache.Get(key);
        if (cached is not null) return cached;
        try
        {
            var bytes = await Task.Run(() =>
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is null) return null;
                using var bitmap = icon.ToBitmap();
                using var memory = new MemoryStream();
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                return memory.ToArray();
            }, ct);
            if (bytes is null) return null;
            using var stream = new MemoryStream(bytes);
            var source = new BitmapImage { DecodePixelWidth = 64 };
            await source.SetSourceAsync(stream.AsRandomAccessStream()).AsTask(ct);
            cache.Put(key, source);
            return source;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { IconFailed(logger, ex); return null; }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Card thumbnail decoding failed for item {ItemId}")]
    private static partial void ThumbnailFailed(ILogger logger, Exception exception, long itemId);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Source application icon could not be decoded")]
    private static partial void IconFailed(ILogger logger, Exception exception);
}
