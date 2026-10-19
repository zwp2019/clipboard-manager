namespace ClipboardManager.Domain;

/// <summary>
/// 剪贴板历史条目（规范 §10）。
/// 纯领域模型，不依赖任何基础设施。
/// </summary>
public sealed class ClipboardItem
{
    public long Id { get; set; }

    public string? Title { get; set; }

    public string? OcrText { get; set; }

    public ClipboardType Type { get; set; } = ClipboardType.Text;

    /// <summary>纯文本内容（Text / Url / Code 类型）。</summary>
    public string? Text { get; set; }

    /// <summary>HTML 原始片段（Html 类型）。</summary>
    public string? Html { get; set; }

    /// <summary>RTF 原始片段（Rtf 类型）。</summary>
    public string? Rtf { get; set; }

    /// <summary>原始图片文件路径（Image 类型）。</summary>
    public string? ImagePath { get; set; }

    /// <summary>缩略图文件路径（Image 类型）。</summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>文件路径集合（File 类型），JSON 数组序列化。</summary>
    public string? FilePaths { get; set; }

    /// <summary>来源应用名称。</summary>
    public string? SourceApplication { get; set; }

    /// <summary>来源窗口标题。</summary>
    public string? SourceTitle { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>内容指纹（SHA-256），用于去重（规范 §14）。</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>内容大小（字节）。</summary>
    public long Size { get; set; }

    public bool IsFavorite { get; set; }

    /// <summary>自定义分组名称（null 表示未分组，规范 §33）。</summary>
    public string? GroupName { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>
    /// 用于列表展示/搜索的文本预览。
    /// </summary>
    public string? GetPreviewText()
    {
        if (!string.IsNullOrEmpty(Text))
        {
            return Text;
        }

        if (Type == ClipboardType.Image)
        {
            return "[图片]";
        }

        if (Type == ClipboardType.File)
        {
            return FilePaths ?? "[文件]";
        }

        return null;
    }
}
