using System.Text.Json;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;

namespace ClipboardManager.Application;

/// <summary>
/// 归一化器：将原始 ClipboardData 转换为统一的 ClipboardItem（规范 §11）。
/// 图片字节不落库，交由存储服务异步落盘（规范 §20、§21）。
/// </summary>
public sealed class ClipboardNormalizer : IClipboardNormalizer
{
    public ClipboardItem Normalize(ClipboardData data, string hash)
    {
        var now = DateTime.Now;
        var item = new ClipboardItem
        {
            Type = data.Type,
            Text = data.Text,
            Html = data.Html,
            Rtf = data.Rtf,
            Hash = hash,
            Size = data.Size,
            SourceApplication = data.SourceApplication,
            SourceTitle = data.SourceTitle,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (data.FilePaths is { Count: > 0 })
        {
            item.FilePaths = JsonSerializer.Serialize(data.FilePaths);
        }

        return item;
    }
}
