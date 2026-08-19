using ClipboardManager.Data;

namespace ClipboardManager.Tests;

/// <summary>
/// 每个测试方法独立的临时 SQLite 数据库（集成测试基础设施）。
/// </summary>
public sealed class TestDatabase : IDisposable
{
    public string Directory { get; }

    public SqliteConnectionFactory Factory { get; }

    public TestDatabase()
    {
        Directory = Path.Combine(Path.GetTempPath(), "ClipboardManager.Tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        var dbPath = Path.Combine(Directory, "test.db");
        Factory = new SqliteConnectionFactory(dbPath);

        var initializer = new DatabaseInitializer(Factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseInitializer>.Instance);
        initializer.Initialize();
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch
        {
            // 清理失败可忽略
        }
    }
}
