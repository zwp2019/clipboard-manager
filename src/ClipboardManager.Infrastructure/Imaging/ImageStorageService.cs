using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using ClipboardManager.Contracts;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Infrastructure;

/// <summary>
/// 图片存储服务：原图保存到 Images 目录，缩略图保存到 Thumbnails 目录（规范 §20、§22）。
/// 全程异步处理，杜绝 UI 线程处理大图（规范 §21）。
/// 目录结构：
///   AppData/Images/{yyyyMM}/xxx.png
///   AppData/Thumbnails/{yyyyMM}/xxx.png
/// </summary>
public sealed partial class ImageStorageService : IImageStorageService
{
    private const int ThumbnailMaxSide = 256;

    private readonly string _imagesRoot;
    private readonly string _thumbnailsRoot;
    private readonly ILogger<ImageStorageService> _logger;

    public ImageStorageService(string dataRoot, ILogger<ImageStorageService> logger)
    {
        _imagesRoot = Path.Combine(dataRoot, "Images");
        _thumbnailsRoot = Path.Combine(dataRoot, "Thumbnails");
        _logger = logger;
    }

    public async Task<ImageSaveResult> SaveImageAsync(byte[] imageBytes, string extension, CancellationToken ct)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image bytes are empty.", nameof(imageBytes));
        }

        var id = Guid.NewGuid().ToString("N");
        var subDirectory = DateTime.Now.ToString("yyyyMM", CultureInfo.InvariantCulture);

        var imagesDir = Path.Combine(_imagesRoot, subDirectory);
        var thumbsDir = Path.Combine(_thumbnailsRoot, subDirectory);
        Directory.CreateDirectory(imagesDir);
        Directory.CreateDirectory(thumbsDir);

        var imagePath = Path.Combine(imagesDir, $"{id}.{NormalizeExtension(extension)}");
        await File.WriteAllBytesAsync(imagePath, imageBytes, ct);

        var thumbnailPath = Path.Combine(thumbsDir, $"{id}.png");
        await Task.Run(() => GenerateThumbnail(imagePath, thumbnailPath), ct);

        Log.ImageSaved(_logger, imagePath, thumbnailPath);
        return new ImageSaveResult(imagePath, thumbnailPath);
    }

    public Task<string?> GenerateThumbnailAsync(string originalImagePath, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(originalImagePath) || !File.Exists(originalImagePath))
        {
            return Task.FromResult<string?>(null);
        }

        var thumbnailPath = BuildThumbnailPath(originalImagePath);
        return Task.Run(
            () =>
            {
                try
                {
                    GenerateThumbnail(originalImagePath, thumbnailPath);
                    return (string?)thumbnailPath;
                }
                catch (Exception ex)
                {
                    // 单张图片损坏/无法解码时跳过，不影响引擎（规范 §45）。
                    Log.ThumbnailFailed(_logger, ex, originalImagePath);
                    return null;
                }
            },
            ct);
    }

    private string BuildThumbnailPath(string originalImagePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(originalImagePath) + ".png";
        var subDirectory = Path.GetFileName(Path.GetDirectoryName(originalImagePath)) ?? DateTime.Now.ToString("yyyyMM", CultureInfo.InvariantCulture);
        var dir = Path.Combine(_thumbnailsRoot, subDirectory);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, fileName);
    }

    private static void GenerateThumbnail(string sourcePath, string destPath)
    {
        using var original = new Bitmap(sourcePath);

        var ratio = Math.Min(1.0, (double)ThumbnailMaxSide / Math.Max(original.Width, original.Height));
        var width = Math.Max(1, (int)(original.Width * ratio));
        var height = Math.Max(1, (int)(original.Height * ratio));

        using var thumbnail = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(thumbnail))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.DrawImage(original, 0, 0, width, height);
        }

        thumbnail.Save(destPath, ImageFormat.Png);
    }

    private static string NormalizeExtension(string? extension)
        => string.IsNullOrWhiteSpace(extension) ? "png" : extension.Trim().TrimStart('.').ToLowerInvariant();

    /// <summary>
    /// 源生成日志方法，避免 CA1848/CA1873。
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(EventId = 0, Level = LogLevel.Debug, Message = "Image saved: {Path} (thumbnail {Thumb})")]
        public static partial void ImageSaved(ILogger logger, string path, string thumb);

        [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to generate thumbnail for {Path}")]
        public static partial void ThumbnailFailed(ILogger logger, Exception exception, string path);
    }
}
