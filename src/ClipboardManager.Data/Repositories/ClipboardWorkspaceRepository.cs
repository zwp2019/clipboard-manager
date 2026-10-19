using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using Dapper;

namespace ClipboardManager.Data;

public sealed partial class ClipboardItemRepository
{
    public async Task<IReadOnlyList<ClipboardItem>> QueryAsync(ClipboardQuery query, int offset, int limit, CancellationToken ct)
    {
        var conditions = new List<string> { "i.is_deleted = 0" };
        var args = new DynamicParameters(new { offset = Math.Max(0, offset), limit = Math.Clamp(limit, 1, 200) });
        if (query.Type is { } type) { conditions.Add("i.type = @type"); args.Add("type", type); }
        if (query.FavoritesOnly) conditions.Add("i.is_favorite = 1");
        if (query.Group is not null) { conditions.Add("i.group_name = @group COLLATE NOCASE"); args.Add("group", query.Group); }
        if (query.Since is { } since) { conditions.Add("i.created_at >= @since"); args.Add("since", since); }
        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            conditions.Add("i.source_application LIKE @source ESCAPE '\\'");
            args.Add("source", $"%{ChineseTokenizer.EscapeLike(query.Source.Trim())}%");
        }
        var text = query.Text.Trim();
        if (text.Length > 0)
        {
            args.Add("like", $"%{ChineseTokenizer.EscapeLike(text)}%");
            var metadata = "(i.title LIKE @like ESCAPE '\\' OR i.source_application LIKE @like ESCAPE '\\' OR i.source_title LIKE @like ESCAPE '\\')";
            var match = text.Length >= 3 ? ChineseTokenizer.BuildMatchQuery(text) : string.Empty;
            if (!string.IsNullOrEmpty(match))
            {
                args.Add("match", match);
                conditions.Add($"(i.id IN (SELECT rowid FROM clipboard_search_fts WHERE clipboard_search_fts MATCH @match) OR {metadata})");
            }
            else
            {
                conditions.Add($"({metadata} OR i.text LIKE @like ESCAPE '\\' OR i.file_paths LIKE @like ESCAPE '\\' OR i.ocr_text LIKE @like ESCAPE '\\')");
            }
        }
        var columns = string.Join(", ", Columns.Split(", ").Select(c => "i." + c));
        var sql = $"SELECT {columns} FROM clipboard_items i WHERE {string.Join(" AND ", conditions)} ORDER BY i.created_at DESC, i.id DESC LIMIT @limit OFFSET @offset";
        using var connection = _factory.CreateConnection();
        return (await connection.QueryAsync<ClipboardItem>(new CommandDefinition(sql, args, cancellationToken: ct))).AsList();
    }

    public async Task<IReadOnlyList<Pinboard>> GetPinboardsAsync(CancellationToken ct)
    {
        using var connection = _factory.CreateConnection();
        return (await connection.QueryAsync<Pinboard>(new CommandDefinition(
            "SELECT name, color, position FROM pinboards ORDER BY position, name", cancellationToken: ct))).AsList();
    }

    public async Task SavePinboardAsync(Pinboard board, string? previousName, CancellationToken ct)
    {
        using var connection = _factory.CreateConnection();
        using var transaction = connection.BeginTransaction();
        if (previousName is null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO pinboards(name, color, position) VALUES(@Name, @Color, COALESCE((SELECT MAX(position)+1 FROM pinboards),0))",
                board, transaction, cancellationToken: ct));
        }
        else
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE pinboards SET name=@Name, color=@Color WHERE name=@previousName COLLATE NOCASE; UPDATE clipboard_items SET group_name=@Name WHERE group_name=@previousName COLLATE NOCASE",
                new { board.Name, board.Color, previousName }, transaction, cancellationToken: ct));
        }
        transaction.Commit();
    }

    public async Task DeletePinboardAsync(string name, CancellationToken ct)
    {
        using var connection = _factory.CreateConnection();
        using var transaction = connection.BeginTransaction();
        // Removing a board returns its items to history instead of destroying the content.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE clipboard_items SET group_name=NULL WHERE group_name=@name COLLATE NOCASE; DELETE FROM pinboards WHERE name=@name COLLATE NOCASE",
            new { name }, transaction, cancellationToken: ct));
        transaction.Commit();
    }

    public async Task ReorderPinboardsAsync(IReadOnlyList<string> names, CancellationToken ct)
    {
        using var connection = _factory.CreateConnection();
        using var transaction = connection.BeginTransaction();
        for (var i = 0; i < names.Count; i++)
            await connection.ExecuteAsync(new CommandDefinition("UPDATE pinboards SET position=@i WHERE name=@name COLLATE NOCASE",
                new { i, name = names[i] }, transaction, cancellationToken: ct));
        transaction.Commit();
    }

    public async Task UpdateContentAsync(ClipboardItem item, string searchContent, CancellationToken ct)
    {
        using var connection = _factory.CreateConnection();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE clipboard_items SET title=@Title, text=@Text, html=@Html, rtf=@Rtf, type=@Type, hash=@Hash, size=@Size, ocr_text=@OcrText, updated_at=@UpdatedAt WHERE id=@Id",
            item, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO clipboard_search(id,content) VALUES(@Id,@content) ON CONFLICT(id) DO UPDATE SET content=excluded.content",
            new { item.Id, content = ChineseTokenizer.ToSearchableContent(searchContent) }, transaction, cancellationToken: ct));
        transaction.Commit();
    }

    public async Task ApplyBatchAsync(IReadOnlyList<long> ids, string action, string? group, CancellationToken ct)
    {
        var sql = action switch
        {
            "delete" => "DELETE FROM clipboard_items WHERE id=@id",
            "favorite" => "UPDATE clipboard_items SET is_favorite=1 WHERE id=@id",
            "unfavorite" => "UPDATE clipboard_items SET is_favorite=0 WHERE id=@id",
            "group" => "UPDATE clipboard_items SET group_name=@group WHERE id=@id",
            _ => throw new ArgumentException("Unknown batch operation", nameof(action)),
        };
        using var connection = _factory.CreateConnection();
        using var transaction = connection.BeginTransaction();
        if (action == "group" && group is not null)
            await connection.ExecuteAsync(new CommandDefinition("INSERT OR IGNORE INTO pinboards(name) VALUES(@group)", new { group }, transaction, cancellationToken: ct));
        foreach (var id in ids.Distinct())
            await connection.ExecuteAsync(new CommandDefinition(sql, new { id, group }, transaction, cancellationToken: ct));
        transaction.Commit();
    }
}
