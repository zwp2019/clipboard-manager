using System.Diagnostics;
using ClipboardManager.Data;
using ClipboardManager.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ClipboardManager.Tests;

/// <summary>
/// 性能测试（规范 §19、§53）：验证 10,000 条级别 Insert / Search / Pagination 表现。
/// 100,000 级别请手动运行（dotnet test --filter "Category=Performance"）。
/// </summary>
[Trait("Category", "Performance")]
public sealed class PerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TestDatabase _db;
    private readonly ClipboardItemRepository _repository;

    public PerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _db = new TestDatabase();
        _repository = new ClipboardItemRepository(_db.Factory, NullLogger<ClipboardItemRepository>.Instance);
    }

    [Fact]
    public async Task Insert_10k_IsFast()
    {
        const int count = 10_000;
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < count; i++)
        {
            var item = NewItem($"批量文本内容 {i} - Redis MySQL RabbitMQ Java Spring");
            await _repository.InsertAsync(item, CancellationToken.None);
        }

        sw.Stop();
        _output.WriteLine($"Insert {count}: {sw.ElapsedMilliseconds} ms ({count * 1000.0 / sw.ElapsedMilliseconds:F0} ops/s)");
        Assert.True(sw.ElapsedMilliseconds < 30_000, $"Insert too slow: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Search_After_10k_IsFast()
    {
        for (var i = 0; i < 10_000; i++)
        {
            var item = NewItem($"搜索测试样本 {i} - SpringBoot MyBatis RabbitMQ Redis");
            var id = await _repository.InsertAsync(item, CancellationToken.None);
            await _repository.UpsertSearchContentAsync(id, item.Text!, CancellationToken.None);
        }

        var sw = Stopwatch.StartNew();
        var results = await _repository.SearchAsync("springboot", 0, 50, CancellationToken.None);
        sw.Stop();

        _output.WriteLine($"Search after 10k: {sw.ElapsedMilliseconds} ms, {results.Count} hits");
        Assert.True(sw.ElapsedMilliseconds < 500, $"Search too slow: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Pagination_After_10k_IsFast()
    {
        for (var i = 0; i < 10_000; i++)
        {
            await _repository.InsertAsync(NewItem($"page-{i}"), CancellationToken.None);
        }

        var sw = Stopwatch.StartNew();
        var page = await _repository.GetRecentAsync(5_000, 50, CancellationToken.None);
        sw.Stop();

        _output.WriteLine($"Pagination offset 5000: {sw.ElapsedMilliseconds} ms, {page.Count} items");
        Assert.True(sw.ElapsedMilliseconds < 200, $"Pagination too slow: {sw.ElapsedMilliseconds} ms");
    }

    private static ClipboardItem NewItem(string text)
        => new()
        {
            Type = ClipboardType.Text,
            Text = text,
            Hash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))
                .ToLowerInvariant(),
            Size = text.Length,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };

    public void Dispose() => _db.Dispose();
}
