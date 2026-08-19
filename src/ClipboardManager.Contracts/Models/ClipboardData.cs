using ClipboardManager.Domain;

namespace ClipboardManager.Contracts;

/// <summary>
/// 剪贴板读取后的原始数据（未归一化），由 ClipboardReader 产出。
/// </summary>
public sealed class ClipboardData
{
    public ClipboardType Type { get; set; } = ClipboardType.Text;

    public string? Text { get; set; }

    public string? Html { get; set; }

    public string? Rtf { get; set; }

    /// <summary>图片原始字节（PNG/JPEG 等编码）。</summary>
    public byte[]? ImageBytes { get; set; }

    /// <summary>图片扩展名：png / jpg / bmp。</summary>
    public string? ImageExtension { get; set; }

    public IReadOnlyList<string>? FilePaths { get; set; }

    public string? SourceApplication { get; set; }

    public string? SourceTitle { get; set; }

    public long Size { get; set; }
}

/// <summary>
/// 剪贴板变化事件参数（规范 §12）。
/// </summary>
public sealed class ClipboardChangedEventArgs : EventArgs
{
    /// <summary>用于区分是否为本程序自身写入（防死循环，规范 §13）。</summary>
    public required string OperationId { get; init; }
}
