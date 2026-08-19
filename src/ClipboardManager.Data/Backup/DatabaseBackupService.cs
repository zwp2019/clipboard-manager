using ClipboardManager.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Data;

/// <summary>
/// 数据库备份/恢复（规范 §16、§46）。
/// 备份：VACUUM INTO 生成一致性快照；恢复：校验后替换当前库（需调用方暂停写入）。
/// </summary>
public sealed class DatabaseBackupService : IBackupService
{
    private readonly SqliteConnectionFactory _factory;
    private readonly ILogger<DatabaseBackupService> _logger;

    public DatabaseBackupService(SqliteConnectionFactory factory, ILogger<DatabaseBackupService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<string> CreateBackupAsync(string destinationDirectory, CancellationToken ct)
    {
        Directory.CreateDirectory(destinationDirectory);
        var backupPath = Path.Combine(
            destinationDirectory,
            $"clipboard-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db");

        // VACUUM INTO 需要路径中不含单引号。
        var escaped = backupPath.Replace("'", "''");

        using var connection = _factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"VACUUM INTO '{escaped}'";
        await command.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Backup created at {Path}", backupPath);
        return backupPath;
    }

    public async Task RestoreAsync(string backupFilePath, CancellationToken ct)
    {
        if (!File.Exists(backupFilePath))
        {
            throw new FileNotFoundException("Backup file not found", backupFilePath);
        }

        // 校验备份文件是可用的 SQLite 库
        using (var probe = new SqliteConnection($"Data Source={backupFilePath};Mode=ReadOnly"))
        {
            await probe.OpenAsync(ct);
            using var command = probe.CreateCommand();
            command.CommandText = "PRAGMA quick_check";
            var result = (await command.ExecuteScalarAsync(ct))?.ToString();
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Backup file integrity check failed: {result}");
            }
        }

        // 调用方需确保当前无活跃写入（恢复前停止 Pipeline）。
        File.Copy(backupFilePath, _factory.DatabaseFilePath, overwrite: true);
        _logger.LogInformation("Database restored from {Path}", backupFilePath);
    }
}
