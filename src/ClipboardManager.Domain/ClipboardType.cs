namespace ClipboardManager.Domain;

/// <summary>
/// 剪贴板内容类型（规范 §10）。
/// </summary>
public enum ClipboardType
{
    Text = 0,
    Image = 1,
    Html = 2,
    Rtf = 3,
    Url = 4,
    File = 5,
    Code = 6,
    Unknown = 7,
}
