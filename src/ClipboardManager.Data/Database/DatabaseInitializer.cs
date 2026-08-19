using Microsoft.Extensions.Logging;

namespace ClipboardManager.Data;

/// <summary>
/// 数据库初始化器：确保数据目录存在、执行迁移、健康检查（规范 §16、§45 Recovery）。
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly SqliteConnectionFactory _factory;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(SqliteConnectionFactory factory, ILogger<DatabaseInitializer> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(_factory.DatabaseFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = _factory.CreateConnection();

        try
        {
            var runner = new MigrationRunner();
            runner.Migrate(connection);
            _logger.LogInformation("Database initialized at {Path}", _factory.DatabaseFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Database migration failed");
            throw;
        }

        // 完整性检查（规范 §45 Crash Recovery）
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA quick_check";
            var result = command.ExecuteScalar()?.ToString();
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Database integrity check failed: {Result}", result);
            }
        }
    }
}
