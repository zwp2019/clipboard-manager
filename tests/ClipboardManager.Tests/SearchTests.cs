using ClipboardManager.Data;
using ClipboardManager.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClipboardManager.Tests;

/// <summary>
/// FTS5 全文搜索测试：中文、英文、数字、代码片段（规范 §18）。
/// </summary>
public sealed class SearchTests : IDisposable
{
    private readonly TestDatabase _db;
    private readonly ClipboardItemRepository _repository;

    private static readonly (ClipboardType Type, string Text)[] Samples =
    {
        (ClipboardType.Text, "RabbitMQ consumer 配置与使用指南"),
        (ClipboardType.Text, "RabbitMQ 延迟队列实现方案"),
        (ClipboardType.Code, "public void ConfigureServices(IServiceCollection services)"),
        (ClipboardType.Text, "Redis 缓存击穿与雪崩处理"),
        (ClipboardType.Text, "MySQL 索引优化与 SQL 调优"),
        (ClipboardType.Url, "https://github.com/example/project"),
        (ClipboardType.Text, "JAVA_HOME=C:\\Program Files\\Java\\jdk-21"),
    };

    public SearchTests()
    {
        _db = new TestDatabase();
        _repository = new ClipboardItemRepository(_db.Factory, NullLogger<ClipboardItemRepository>.Instance);
    }

    [Fact]
    public async Task Search_EnglishKeyword_FindsMatch()
    {
        await SeedAsync();

        var results = await _repository.SearchAsync("rabbitmq", 0, 20, CancellationToken.None);

        Assert.NotNull(results);
        Assert.True(results.Count >= 2);
        Assert.All(results, r => Assert.Contains("RabbitMQ", r.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_Chinese_Works()
    {
        await SeedAsync();

        // "缓存" 为 2 字符 → LIKE 兜底；"延迟队列" 走 FTS
        var results = await _repository.SearchAsync("延迟队列", 0, 20, CancellationToken.None);

        Assert.NotNull(results);
        Assert.Contains(results, r => r.Text!.Contains("延迟队列"));
    }

    [Fact]
    public async Task Search_ShortChinese_LikeFallback()
    {
        await SeedAsync();

        var results = await _repository.SearchAsync("缓存", 0, 20, CancellationToken.None);

        Assert.NotNull(results);
        Assert.Contains(results, r => r.Text!.Contains("缓存"));
    }

    [Fact]
    public async Task Search_CodeSnippet_Works()
    {
        await SeedAsync();

        var results = await _repository.SearchAsync("configuredservices", 0, 20, CancellationToken.None);

        Assert.NotNull(results);
        Assert.Contains(results, r => r.Text!.Contains("ConfigureServices"));
    }

    [Fact]
    public async Task Search_Url_Works()
    {
        await SeedAsync();

        var results = await _repository.SearchAsync("github.com", 0, 20, CancellationToken.None);

        Assert.NotNull(results);
        Assert.Contains(results, r => r.Text!.Contains("github.com"));
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmpty()
    {
        await SeedAsync();

        var results = await _repository.SearchAsync("不存在的关键字xyzzy", 0, 20, CancellationToken.None);

        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_EmptyQuery_ReturnsEmpty()
    {
        var results = await _repository.SearchAsync("  ", 0, 20, CancellationToken.None);

        Assert.Empty(results);
    }

    private async Task SeedAsync()
    {
        foreach (var (type, text) in Samples)
        {
            var item = new ClipboardItem
            {
                Type = type,
                Text = text,
                Hash = Guid.NewGuid().ToString("N"),
                Size = text.Length,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
            };

            var id = await _repository.InsertAsync(item, CancellationToken.None);
            await _repository.UpsertSearchContentAsync(id, text, CancellationToken.None);
        }
    }

    public void Dispose() => _db.Dispose();
}
