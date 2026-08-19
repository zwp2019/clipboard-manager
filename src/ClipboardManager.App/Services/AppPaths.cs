namespace ClipboardManager.App;

/// <summary>
/// 应用数据目录（规范 §20）：%LocalAppData%\ClipboardManager
///   Database / Images / Thumbnails / Cache / Logs / Backups
/// </summary>
public sealed class AppPaths
{
    public string Root { get; }

    public string Database { get; }

    public string Data { get; }

    public string Images { get; }

    public string Thumbnails { get; }

    public string Cache { get; }

    public string Logs { get; }

    public string Backups { get; }

    public AppPaths()
    {
        Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipboardManager");
        Database = Path.Combine(Root, "Database", "clipboard.db");
        Data = Path.Combine(Root, "Data");
        Images = Path.Combine(Root, "Images");
        Thumbnails = Path.Combine(Root, "Thumbnails");
        Cache = Path.Combine(Root, "Cache");
        Logs = Path.Combine(Root, "Logs");
        Backups = Path.Combine(Root, "Backups");

        foreach (var dir in new[] { Data, Images, Thumbnails, Cache, Logs, Backups, Path.GetDirectoryName(Database)! })
        {
            Directory.CreateDirectory(dir);
        }
    }
}
