using ClipboardManager.Application;
using ClipboardManager.Data;
using ClipboardManager.Domain;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClipboardManager.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly ClipboardItemRepository _repository;
    private readonly WorkspaceService _service;
    public WorkspaceTests()
    {
        _repository = new(_db.Factory, NullLogger<ClipboardItemRepository>.Instance);
        _service = new(_repository, _repository, new ClipboardHashService());
    }
    private async Task<ClipboardItem> AddAsync(string text, string? group = null, DateTime? date = null, string source = "code.exe")
    {
        var item = new ClipboardItem { Text = text, Hash = Guid.NewGuid().ToString(), GroupName = group, CreatedAt = date ?? DateTime.Now, SourceApplication = source };
        item.Id = await _repository.InsertAsync(item, default);
        await _repository.UpsertSearchContentAsync(item.Id, text, default);
        return item;
    }
    [Fact]
    public async Task EmptyBoardSurvivesRefreshAndRenameMovesItems()
    {
        await _service.SavePinboardAsync(new() { Name = "Work" }, null, default);
        Assert.Equal("Work", Assert.Single(await _repository.GetPinboardsAsync(default)).Name);
        var item = await AddAsync("important", "Work");
        await _service.SavePinboardAsync(new() { Name = "Project", Color = "#49A7A0" }, "Work", default);
        Assert.Equal("Project", (await _repository.GetByIdAsync(item.Id, default))!.GroupName);
        Assert.Equal("#49A7A0", Assert.Single(await _repository.GetPinboardsAsync(default)).Color);
        await _service.DeletePinboardAsync("Project", default);
        Assert.Empty(await _repository.GetPinboardsAsync(default));
        Assert.Null((await _repository.GetByIdAsync(item.Id, default))!.GroupName);
    }
    [Fact]
    public async Task BoardItemsSurviveAgeAndCountCleanup()
    {
        var pinned = await AddAsync("keep", "Work", DateTime.Now.AddDays(-90));
        await AddAsync("expire", date: DateTime.Now.AddDays(-90));
        await AddAsync("new");
        Assert.Equal(1, await _repository.DeleteOlderThanAsync(DateTime.Now.AddDays(-30), default));
        await _repository.TrimToLimitAsync(1, default);
        Assert.NotNull(await _repository.GetByIdAsync(pinned.Id, default));
    }
    [Fact]
    public async Task QueryCombinesKeywordBoardSourceDateAndPagination()
    {
        for (var i = 0; i < 85; i++) await AddAsync($"RabbitMQ 配置 {i}", "Work");
        await AddAsync("RabbitMQ other app", "Work", source: "chrome.exe");
        await AddAsync("RabbitMQ old", "Work", DateTime.Today.AddDays(-4));
        await AddAsync("RabbitMQ another board", "Other");
        var query = new ClipboardQuery { Text = "rabbitmq", Group = "Work", Source = "code", Since = DateTime.Today };
        var first = await _repository.QueryAsync(query, 0, 80, default);
        var next = await _repository.QueryAsync(query, 80, 80, default);
        Assert.Equal(80, first.Count); Assert.Equal(5, next.Count);
        Assert.Empty(first.Select(i => i.Id).Intersect(next.Select(i => i.Id)));
    }
    [Fact]
    public async Task EditingUpdatesHashAndIndexWhileRenameKeepsRichText()
    {
        var item = await AddAsync("oldword");
        item.Type = ClipboardType.Html; item.Html = "<b>oldword</b>";
        await _repository.UpdateContentAsync(item, "oldword", default);
        await _service.SaveTextAsync(item, "说明", "oldword", default);
        var renamed = (await _repository.GetByIdAsync(item.Id, default))!;
        Assert.Equal("<b>oldword</b>", renamed.Html);
        await _service.SaveTextAsync(renamed, "说明", "newword", default);
        var edited = (await _repository.GetByIdAsync(item.Id, default))!;
        Assert.Null(edited.Html); Assert.Equal(ClipboardType.Text, edited.Type); Assert.NotEqual(item.Hash, edited.Hash);
        Assert.Empty(await _repository.QueryAsync(new() { Text = "oldword" }, 0, 80, default));
        Assert.Single(await _repository.QueryAsync(new() { Text = "newword" }, 0, 80, default));
        Assert.Single(await _repository.QueryAsync(new() { Text = "说明" }, 0, 80, default));
    }
    [Fact]
    public async Task BatchGroupsAndFavoritesAllSelectedItems()
    {
        var a = await AddAsync("one"); var b = await AddAsync("two"); var c = await AddAsync("three");
        long[] ids = [a.Id, b.Id];
        await _repository.ApplyBatchAsync(ids, "group", "Board", default);
        await _repository.ApplyBatchAsync(ids, "favorite", null, default);
        Assert.Equal(2, (await _repository.QueryAsync(new() { Group = "Board", FavoritesOnly = true }, 0, 80, default)).Count);
        await _repository.ApplyBatchAsync(ids, "delete", null, default);
        Assert.NotNull(await _repository.GetByIdAsync(c.Id, default));
        Assert.Empty(await _repository.QueryAsync(new() { Group = "Board" }, 0, 80, default));
        Assert.Single(await _repository.GetPinboardsAsync(default));
    }
    [Fact]
    public async Task UpgradeFromV2PreservesDataAndImportsBoards()
    {
        var factory = new SqliteConnectionFactory(Path.Combine(_db.Directory, "legacy.db"));
        using var connection = factory.CreateConnection();
        connection.Execute("CREATE TABLE schema_version(version INTEGER NOT NULL, applied_at TEXT NOT NULL)");
        foreach (var migration in SqliteSchema.Migrations.Where(m => m.Version <= 2))
        {
            connection.Execute(migration.Sql);
            connection.Execute("INSERT INTO schema_version VALUES(@Version, '')", new { migration.Version });
        }
        connection.Execute("INSERT INTO clipboard_items(type,text,hash,created_at,updated_at,group_name) VALUES(0,'legacy','abc','2026-01-01','2026-01-01','Work')");
        MigrationRunner.Migrate(connection); MigrationRunner.Migrate(connection);
        Assert.Equal("legacy", connection.QuerySingle<string>("SELECT text FROM clipboard_items"));
        Assert.Equal("Work", connection.QuerySingle<string>("SELECT name FROM pinboards"));
        Assert.Equal(3, connection.QuerySingle<int>("SELECT MAX(version) FROM schema_version"));
    }
    [Fact]
    public async Task OcrTextBecomesSearchableAndWildcardsStayLiteral()
    {
        var image = await AddAsync("[图片]"); image.Type = ClipboardType.Image; image.OcrText = "会议纪要 Architecture";
        await _repository.UpdateContentAsync(image, image.OcrText, default);
        Assert.Single(await _repository.QueryAsync(new() { Text = "会议纪要" }, 0, 80, default));
        Assert.Empty(await _repository.QueryAsync(new() { Text = "%_" }, 0, 80, default));
    }
    public void Dispose() => _db.Dispose();
}
