using ClipboardManager.Domain;

namespace ClipboardManager.Contracts;

/// <summary>
/// 存储服务：编排仓储、图片存储、搜索索引，处理去重（规范 §11、§15）。
/// </summary>
public interface IClipboardStorageService
{
    /// <param name="imageBytes">图片原始字节（Image 类型时提供，用于异步落盘，规范 §21）。</param>
    /// <param name="imageExtension">图片扩展名：png / jpg / bmp。</param>
    Task<ClipboardItem> SaveAsync(
        ClipboardItem item,
        byte[]? imageBytes,
        string? imageExtension,
        CancellationToken ct);
}

/// <summary>
/// 图片存储服务：保存原图到文件系统、生成缩略图（规范 §20-§22）。
/// </summary>
public interface IImageStorageService
{
    Task<ImageSaveResult> SaveImageAsync(byte[] imageBytes, string extension, CancellationToken ct);

    /// <summary>为已存在的原图生成/刷新缩略图。</summary>
    Task<string?> GenerateThumbnailAsync(string originalImagePath, CancellationToken ct);
}

/// <summary>
/// 图片存储结果。
/// </summary>
public sealed class ImageSaveResult
{
    public string ImagePath { get; }

    public string? ThumbnailPath { get; }

    public ImageSaveResult(string imagePath, string? thumbnailPath)
    {
        ImagePath = imagePath;
        ThumbnailPath = thumbnailPath;
    }
}
