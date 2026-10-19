using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using Dapper;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Data;

/// <summary>
/// 剪贴板条目仓储：SQLite + Dapper 实现（规范 §16-§19）。
/// 搜索委托 FTS5，短查询自动降级为 LIKE 兜底。
/// </summary>
public sealed partial class ClipboardItemRepository : IClipboardItemRepository, IWorkspaceRepository
{
    private const string Columns =
        "id, type, text, html, rtf, image_path, thumbnail_path, file_paths, " +
        "source_application, source_title, created_at, updated_at, hash, size, is_favorite, group_name, is_deleted, title, ocr_text";

    private readonly SqliteConnectionFactory _factory;
    private readonly ILogger<ClipboardItemRepository> _logger;

    public ClipboardItemRepository(SqliteConnectionFactory factory, ILogger<ClipboardItemRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<long> InsertAsync(ClipboardItem item, CancellationToken ct)
    {
        const string sql =
            """
            INSERT INTO clipboard_items
                (type, text, html, rtf, image_path, thumbnail_path, file_paths,
                 source_application, source_title, created_at, updated_at, hash, size,
                 is_favorite, group_name, is_deleted, title, ocr_text)
            VALUES
                (@Type, @Text, @Html, @Rtf, @ImagePath, @ThumbnailPath, @FilePaths,
                 @SourceApplication, @SourceTitle, @CreatedAt, @UpdatedAt, @Hash, @Size,
                 @IsFavorite, @GroupName, @IsDeleted, @Title, @OcrText);
            SELECT last_insert_rowid();
            """;

        using var connection = _factory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, item, cancellationToken: ct));
    }

    public async Task<ClipboardItem?> GetByIdAsync(long id, CancellationToken ct)
    {
        const string sql = $"SELECT {Columns} FROM clipboard_items WHERE id = @id AND is_deleted = 0";
        using var connection = _factory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ClipboardItem>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task<ClipboardItem?> FindByHashAsync(string hash, CancellationToken ct)
    {
        const string sql = $"SELECT {Columns} FROM clipboard_items WHERE hash = @hash AND is_deleted = 0 LIMIT 1";
        using var connection = _factory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ClipboardItem>(
            new CommandDefinition(sql, new { hash }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ClipboardItem>> GetRecentAsync(int offset, int limit, CancellationToken ct)
    {
        const string sql =
            $"SELECT {Columns} FROM clipboard_items WHERE is_deleted = 0 " +
            "ORDER BY created_at DESC, id DESC LIMIT @limit OFFSET @offset";

        using var connection = _factory.CreateConnection();
        var rows = await connection.QueryAsync<ClipboardItem>(
            new CommandDefinition(sql, new { offset, limit }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ClipboardItem>> GetFavoritesAsync(int offset, int limit, CancellationToken ct)
    {
        const string sql =
            $"SELECT {Columns} FROM clipboard_items WHERE is_deleted = 0 AND is_favorite = 1 " +
            "ORDER BY created_at DESC, id DESC LIMIT @limit OFFSET @offset";

        using var connection = _factory.CreateConnection();
        var rows = await connection.QueryAsync<ClipboardItem>(
            new CommandDefinition(sql, new { offset, limit }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ClipboardItem>> GetByTypeAsync(ClipboardType type, int offset, int limit, CancellationToken ct)
    {
        const string sql =
            $"SELECT {Columns} FROM clipboard_items WHERE is_deleted = 0 AND type = @type " +
            "ORDER BY created_at DESC, id DESC LIMIT @limit OFFSET @offset";

        using var connection = _factory.CreateConnection();
        var rows = await connection.QueryAsync<ClipboardItem>(
            new CommandDefinition(sql, new { type, offset, limit }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ClipboardItem>> GetByGroupAsync(string group, int offset, int limit, CancellationToken ct)
    {
        const string sql =
            $"SELECT {Columns} FROM clipboard_items WHERE is_deleted = 0 AND group_name = @group " +
            "ORDER BY created_at DESC, id DESC LIMIT @limit OFFSET @offset";

        using var connection = _factory.CreateConnection();
        var rows = await connection.QueryAsync<ClipboardItem>(
            new CommandDefinition(sql, new { group, offset, limit }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ClipboardItem>> SearchAsync(string query, int offset, int limit, CancellationToken ct)
    {
        // 超短查询使用 LIKE 兜底，避免 FTS5 对 1-2 字符 token 的不确定性。
        if (query.Trim().Length < 3)
        {
            return await SearchByLikeAsync(query.Trim(), offset, limit, ct);
        }

        var match = ChineseTokenizer.BuildMatchQuery(query);
        if (string.IsNullOrEmpty(match))
        {
            return Array.Empty<ClipboardItem>();
        }

        var prefixedColumns = string.Join(", ", Columns.Split(", ").Select(c => "i." + c));
        var sql =
            $"""
             SELECT {prefixedColumns} FROM clipboard_search_fts f
             INNER JOIN clipboard_items i ON i.id = f.rowid
             WHERE clipboard_search_fts MATCH @match AND i.is_deleted = 0
             ORDER BY i.created_at DESC, i.id DESC
             LIMIT @limit OFFSET @offset
             """;

        using var connection = _factory.CreateConnection();
        var rows = await connection.QueryAsync<ClipboardItem>(
            new CommandDefinition(sql, new { match, offset, limit }, cancellationToken: ct));
        return rows.AsList();
    }

    private async Task<IReadOnlyList<ClipboardItem>> SearchByLikeAsync(string query, int offset, int limit, CancellationToken ct)
    {
        var like = $"%{ChineseTokenizer.EscapeLike(query)}%";
        const string sql =
            $"""
             SELECT {Columns} FROM clipboard_items
             WHERE is_deleted = 0
               AND (text LIKE @like ESCAPE '\' OR html LIKE @like ESCAPE '\' OR file_paths LIKE @like ESCAPE '\')
             ORDER BY created_at DESC, id DESC
             LIMIT @limit OFFSET @offset
             """;

        using var connection = _factory.CreateConnection();
        var rows = await connection.QueryAsync<ClipboardItem>(
            new CommandDefinition(sql, new { like, offset, limit }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<int> CountAsync(CancellationToken ct)
    {
        const string sql = "SELECT COUNT(*) FROM clipboard_items WHERE is_deleted = 0";
        using var connection = _factory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task SetFavoriteAsync(long id, bool favorite, CancellationToken ct)
    {
        const string sql = "UPDATE clipboard_items SET is_favorite = @favorite WHERE id = @id";
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(
            new CommandDefinition(sql, new { id, favorite = favorite ? 1 : 0 }, cancellationToken: ct));
    }

    public async Task SetGroupAsync(long id, string? group, CancellationToken ct)
    {
        const string sql = "INSERT OR IGNORE INTO pinboards(name) SELECT @group WHERE @group IS NOT NULL; UPDATE clipboard_items SET group_name = @group WHERE id = @id";
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(
            new CommandDefinition(sql, new { id, group }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<string>> GetGroupNamesAsync(CancellationToken ct)
    {
        const string sql =
            "SELECT name FROM pinboards ORDER BY position, name";

        using var connection = _factory.CreateConnection();
        var rows = await connection.QueryAsync<string>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task TouchAsync(long id, CancellationToken ct)
    {
        const string sql =
            "UPDATE clipboard_items SET created_at = @now, updated_at = @now WHERE id = @id";
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(
            new CommandDefinition(sql, new { id, now = DateTime.Now }, cancellationToken: ct));
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        const string sql = "DELETE FROM clipboard_items WHERE id = @id";
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        const string sql = "DELETE FROM clipboard_items";
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct)
    {
        // 收藏条目不参与过期清理，避免用户误丢（可后续加设置）。
        const string sql =
            "DELETE FROM clipboard_items WHERE created_at < @cutoff AND is_favorite = 0 AND group_name IS NULL";
        using var connection = _factory.CreateConnection();
        return await connection.ExecuteAsync(
            new CommandDefinition(sql, new { cutoff }, cancellationToken: ct));
    }

    public async Task<int> TrimToLimitAsync(int maxItems, CancellationToken ct)
    {
        const string sql =
            """
            DELETE FROM clipboard_items
            WHERE is_favorite = 0 AND group_name IS NULL AND id IN (
                SELECT id FROM clipboard_items
                ORDER BY created_at DESC, id DESC
                LIMIT -1 OFFSET @maxItems
            )
            """;
        using var connection = _factory.CreateConnection();
        return await connection.ExecuteAsync(
            new CommandDefinition(sql, new { maxItems }, cancellationToken: ct));
    }

    public async Task UpsertSearchContentAsync(long id, string content, CancellationToken ct)
    {
        var searchable = ChineseTokenizer.ToSearchableContent(content);
        const string sql =
            """
            INSERT INTO clipboard_search (id, content) VALUES (@id, @content)
            ON CONFLICT(id) DO UPDATE SET content = excluded.content
            """;
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(
            new CommandDefinition(sql, new { id, content = searchable }, cancellationToken: ct));
    }
}
