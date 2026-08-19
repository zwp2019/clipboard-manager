namespace ClipboardManager.Domain;

/// <summary>
/// 标签（规范 §9、§17）。第二阶段功能。
/// </summary>
public sealed class Tag
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
