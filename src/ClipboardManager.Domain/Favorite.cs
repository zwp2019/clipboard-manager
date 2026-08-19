namespace ClipboardManager.Domain;

/// <summary>
/// 收藏关系（规范 §9）。
/// </summary>
public sealed class Favorite
{
    public long ClipboardItemId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
