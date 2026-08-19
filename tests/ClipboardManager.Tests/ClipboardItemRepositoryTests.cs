using ClipboardManager.Data;
using ClipboardManager.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClipboardManager.Tests;

public sealed class ClipboardItemRepositoryTests : IDisposable
{
    private readonly TestDatabase _db;
    private readonly ClipboardItemRepository _repository;

    public ClipboardItemRepositoryTests()
    {
        _db = new TestDatabase();
        _repository = new ClipboardItemRepository(_db.Factory, NullLogger<ClipboardItemRepository>.Instance);
    }

    [Fact]
    public async Task InsertAndGetById()
    {
        var item = NewItem("hello world");
        var id = await _repository.InsertAsync(item, CancellationToken.None);

        var loaded = await _repository.GetByIdAsync(id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("hello world", loaded!.Text);
        Assert.Equal(id, loaded.Id);
    }

    [Fact]
    public async Task GetRecent_OrderedByCreatedDesc()
    {
        var first = await _repository.InsertAsync(NewItem("first"), CancellationToken.None);
        await Task.Delay(20);
        var second = await _repository.InsertAsync(NewItem("second"), CancellationToken.None);

        var recent = await _repository.GetRecentAsync(0, 10, CancellationToken.None);

        Assert.Equal(2, recent.Count);
        Assert.Equal(second, recent[0].Id);
        Assert.Equal(first, recent[1].Id);
    }

    [Fact]
    public async Task FindByHash_ReturnsMatching()
    {
        var item = NewItem("dup-me");
        await _repository.InsertAsync(item, CancellationToken.None);

        var found = await _repository.FindByHashAsync(item.Hash, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(item.Hash, found!.Hash);
    }

    [Fact]
    public async Task Touch_MovesToTop()
    {
        var a = await _repository.InsertAsync(NewItem("a"), CancellationToken.None);
        var b = await _repository.InsertAsync(NewItem("b"), CancellationToken.None);
        await Task.Delay(20);

        await _repository.TouchAsync(a, CancellationToken.None);
        var recent = await _repository.GetRecentAsync(0, 10, CancellationToken.None);

        Assert.Equal(a, recent[0].Id); // 去重后移到最前
    }

    [Fact]
    public async Task SetFavorite_AndGetFavorites()
    {
        var id = await _repository.InsertAsync(NewItem("fav"), CancellationToken.None);
        await _repository.SetFavoriteAsync(id, true, CancellationToken.None);

        var favorites = await _repository.GetFavoritesAsync(0, 10, CancellationToken.None);

        Assert.Single(favorites);
        Assert.True(favorites[0].IsFavorite);
    }

    [Fact]
    public async Task Delete_RemovesItem()
    {
        var id = await _repository.InsertAsync(NewItem("gone"), CancellationToken.None);
        await _repository.DeleteAsync(id, CancellationToken.None);

        Assert.Null(await _repository.GetByIdAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task Clear_EmptiesAll()
    {
        await _repository.InsertAsync(NewItem("x"), CancellationToken.None);
        await _repository.InsertAsync(NewItem("y"), CancellationToken.None);

        await _repository.ClearAsync(CancellationToken.None);

        Assert.Equal(0, await _repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DeleteOlderThan_RemovesOnlyOldNonFavorites()
    {
        var old = NewItem("old");
        old.CreatedAt = DateTime.Now.AddDays(-10);
        await _repository.InsertAsync(old, CancellationToken.None);

        var fav = NewItem("fav-old");
        fav.CreatedAt = DateTime.Now.AddDays(-10);
        var favId = await _repository.InsertAsync(fav, CancellationToken.None);
        await _repository.SetFavoriteAsync(favId, true, CancellationToken.None);

        var fresh = NewItem("fresh");
        await _repository.InsertAsync(fresh, CancellationToken.None);

        var removed = await _repository.DeleteOlderThanAsync(DateTime.Now.AddDays(-5), CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.NotNull(await _repository.GetByIdAsync(favId, CancellationToken.None)); // 收藏保留
    }

    [Fact]
    public async Task TrimToLimit_KeepsNewestNonFavorites()
    {
        for (var i = 0; i < 10; i++)
        {
            await _repository.InsertAsync(NewItem($"item-{i}"), CancellationToken.None);
            await Task.Delay(5);
        }

        var removed = await _repository.TrimToLimitAsync(3, CancellationToken.None);

        Assert.Equal(7, removed);
        Assert.Equal(3, await _repository.CountAsync(CancellationToken.None));
    }

    private static ClipboardItem NewItem(string text)
        => new()
        {
            Type = ClipboardType.Text,
            Text = text,
            Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant(),
            Size = text.Length,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };

    public void Dispose() => _db.Dispose();
}
