using Microsoft.Extensions.Logging;

namespace ClipboardManager.Data;

/// <summary>
/// 数据库初始化器：确保数据目录存在、执行迁移、健康检查（规范 §16、§45 Recovery）。
/// </summary>
public sealed partial class DatabaseInitializer
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
            MigrationRunner.Migrate(connection);
            if (_logger.IsEnabled(LogLevel.Information))
            {
                Log.DbInitialized(_logger, _factory.DatabaseFilePath);
            }
        }
        catch (Exception ex)
        {
            Log.DbInitFailed(_logger, ex);
            throw;
        }

        // 完整性检查（规范 §45 Crash Recovery）
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA quick_check";
            var result = command.ExecuteScalar()?.ToString();
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                Log.IntegrityCheckFailed(_logger, result);
            }
        }
    }

    /// <summary>
    /// 源生成日志方法，避免 CA1848/CA1873（规范 §16）。
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Database initialized at {Path}")]
        public static partial void DbInitialized(ILogger logger, string path);

        [LoggerMessage(EventId = 1, Level = LogLevel.Critical, Message = "Database migration failed")]
        public static partial void DbInitFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Database integrity check failed: {Result}")]
        public static partial void IntegrityCheckFailed(ILogger logger, string? result);
    }
}
