using ClipboardManager.Contracts;
using Dapper;

namespace ClipboardManager.Data;

/// <summary>
/// 设置仓储：settings 表 key-value 读写。
/// </summary>
public sealed class SettingsRepository : ISettingsRepository
{
    private readonly SqliteConnectionFactory _factory;

    public SettingsRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        const string sql = "SELECT value FROM settings WHERE key = @key";
        using var connection = _factory.CreateConnection();
        return await connection.ExecuteScalarAsync<string?>(
            new CommandDefinition(sql, new { key }, cancellationToken: ct));
    }

    public async Task SetAsync(string key, string value, CancellationToken ct)
    {
        const string sql =
            """
            INSERT INTO settings (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(
            new CommandDefinition(sql, new { key, value }, cancellationToken: ct));
    }
}
