using Microsoft.Data.Sqlite;

namespace ClipboardManager.Data;

/// <summary>
/// SQLite 连接工厂：统一 PRAGMA 配置（WAL、busy_timeout、foreign_keys、synchronous）（规范 §16）。
/// </summary>
public sealed class SqliteConnectionFactory
{
    static SqliteConnectionFactory()
    {
        // 数据库列名为 snake_case（image_path / created_at 等），
        // 必须启用下划线映射，否则实体属性全部静默为 null/默认值。
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    private readonly string _connectionString;

    public SqliteConnectionFactory(string databaseFilePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        };
        _connectionString = builder.ToString();
    }

    public string DatabaseFilePath { get; }

    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        ConfigurePragmas(connection);
        return connection;
    }

    private static void ConfigurePragmas(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            """;
        command.ExecuteNonQuery();
    }
}
