using System.Collections.Concurrent;
using System.Threading.Channels;
using ClipboardManager.Application;
using ClipboardManager.Contracts;
using ClipboardManager.Data;
using ClipboardManager.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClipboardManager.Tests;

public sealed class InternalClipboardWriteTests
{
    [Fact]
    public void MarkerSurvivesDisposedScopeAndIsSessionSpecific()
    {
        var guard = new InternalClipboardGuard();
        string marker;
        using (guard.BeginOwnWrite("copy-history")) marker = guard.CreateClipboardMarker("copy-history");
        Assert.False(guard.IsOwnWrite);
        Assert.True(guard.IsOwnClipboardMarker(marker));
        Assert.False(new InternalClipboardGuard().IsOwnClipboardMarker(marker));
        Assert.False(guard.IsOwnClipboardMarker(null));
        Assert.False(guard.IsOwnClipboardMarker(""));
    }

    [Fact]
    public void OverlappingWriteScopesDoNotClearEachOther()
    {
        var guard = new InternalClipboardGuard();
        var first = guard.BeginOwnWrite("first");
        using var second = guard.BeginOwnWrite("second");
        first.Dispose(); first.Dispose();
        Assert.True(guard.IsOwnWrite);
        Assert.Equal("second", guard.CurrentOperationId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DelayedOwnNotificationDoesNotInsertEvenIfRichTextHashChanged(bool deduplicate)
    {
        using var fixture = new PipelineFixture(deduplicate);
        string marker;
        using (fixture.Guard.BeginOwnWrite("replay")) marker = fixture.Guard.CreateClipboardMarker("replay");
        Assert.False(fixture.Guard.IsOwnWrite);
        fixture.Copy(new ClipboardData { Type = ClipboardType.Html, Text = "old message", Html = "<html><body><b>old message</b></body></html>", Size = 100, InternalWriteMarker = marker });
        await fixture.Reader.Reads.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        fixture.Copy(new ClipboardData { Text = "new external message", Size = 20 });
        var saved = await fixture.Storage.Saved.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("new external message", saved.Text);
        Assert.Single(fixture.Storage.Items);
    }

    [Fact]
    public async Task ExternalCopyDuringWriteScopeIsNotLost()
    {
        using var fixture = new PipelineFixture(true);
        using var scope = fixture.Guard.BeginOwnWrite("still-writing");
        fixture.Copy(new ClipboardData { Text = "external copy", Size = 13 });
        var saved = await fixture.Storage.Saved.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("external copy", saved.Text);
    }

    [Fact]
    public async Task MarkerFromAnotherSessionDoesNotSuppressExternalContent()
    {
        using var fixture = new PipelineFixture(true);
        fixture.Copy(new ClipboardData { Text = "external", Size = 8, InternalWriteMarker = new InternalClipboardGuard().CreateClipboardMarker("other") });
        var saved = await fixture.Storage.Saved.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("external", saved.Text);
    }

    [Fact]
    public async Task KeepDuplicatesSettingPreservesSeparateExternalCopies()
    {
        using var fixture = new PipelineFixture(false);
        for (var i = 0; i < 2; i++)
        {
            fixture.Copy(new ClipboardData { Text = "same content", Size = 12 });
            await fixture.Storage.Saved.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        }
        Assert.Equal(2, fixture.Storage.Items.Count);
    }

    [Fact]
    public async Task RecopiedTextReusesExistingIdAndPreservesSavedMetadata()
    {
        using var db = new TestDatabase();
        var repository = new ClipboardItemRepository(db.Factory, NullLogger<ClipboardItemRepository>.Instance);
        var images = new FakeImages();
        var service = new ClipboardStorageService(repository, images, new FakeSettings(), NullLogger<ClipboardStorageService>.Instance);
        var hash = new ClipboardHashService().ComputeHash(new ClipboardData { Text = "history message" });
        var original = new ClipboardItem { Text = "history message", Hash = hash, Title = "keep this title", IsFavorite = true, GroupName = "Work", CreatedAt = DateTime.Now.AddDays(-3) };
        original.Id = await repository.InsertAsync(original, default);
        var result = await service.SaveAsync(new ClipboardItem { Text = "history message", Hash = hash }, null, null, default);
        var loaded = (await repository.GetByIdAsync(original.Id, default))!;
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(1, await repository.CountAsync(default));
        Assert.Equal("keep this title", loaded.Title);
        Assert.True(loaded.IsFavorite); Assert.Equal("Work", loaded.GroupName);
        Assert.True(loaded.CreatedAt > original.CreatedAt);
    }

    [Fact]
    public async Task RepeatedImageDoesNotWriteUnusedImageFiles()
    {
        using var db = new TestDatabase();
        var repository = new ClipboardItemRepository(db.Factory, NullLogger<ClipboardItemRepository>.Instance);
        var images = new FakeImages();
        var service = new ClipboardStorageService(repository, images, new FakeSettings(), NullLogger<ClipboardStorageService>.Instance);
        var original = new ClipboardItem { Type = ClipboardType.Image, Hash = "same-image", ImagePath = "existing.png" };
        original.Id = await repository.InsertAsync(original, default);
        var result = await service.SaveAsync(new ClipboardItem { Type = ClipboardType.Image, Hash = original.Hash }, [1, 2, 3], "png", default);
        Assert.Equal(original.Id, result.Id);
        Assert.Equal(0, images.SaveCalls);
        Assert.Equal(1, await repository.CountAsync(default));
    }

    private sealed class PipelineFixture : IDisposable
    {
        private readonly FakeMonitor _monitor = new();
        private readonly ClipboardPipelineService _pipeline;
        public InternalClipboardGuard Guard { get; } = new();
        public FakeReader Reader { get; } = new();
        public FakeStorage Storage { get; } = new();
        public PipelineFixture(bool deduplicate)
        {
            var settings = new FakeSettings(); settings.Current.EnableDeduplication = deduplicate;
            _pipeline = new ClipboardPipelineService(_monitor, Reader, new ClipboardNormalizer(), new ClipboardHashService(), Storage,
                new ClipboardDeduplicationService(), settings, Guard, NullLogger<ClipboardPipelineService>.Instance);
            _pipeline.Start();
        }
        public void Copy(ClipboardData data) { Reader.Data.Enqueue(data); _monitor.Raise(); }
        public void Dispose() => _pipeline.Stop();
    }
    private sealed class FakeMonitor : IClipboardMonitor
    {
        public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;
        public bool IsRunning { get; private set; }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Raise() => ClipboardChanged?.Invoke(this, new ClipboardChangedEventArgs { OperationId = Guid.NewGuid().ToString("N") });
    }
    private sealed class FakeReader : IClipboardReader
    {
        public ConcurrentQueue<ClipboardData> Data { get; } = new();
        public Channel<bool> Reads { get; } = Channel.CreateUnbounded<bool>();
        public Task<ClipboardData?> ReadAsync(CancellationToken cancellationToken)
        {
            Data.TryDequeue(out var data); Reads.Writer.TryWrite(true);
            return Task.FromResult(data);
        }
    }
    private sealed class FakeStorage : IClipboardStorageService
    {
        public ConcurrentQueue<ClipboardItem> Items { get; } = new();
        public Channel<ClipboardItem> Saved { get; } = Channel.CreateUnbounded<ClipboardItem>();
        public Task<ClipboardItem> SaveAsync(ClipboardItem item, byte[]? imageBytes, string? imageExtension, CancellationToken ct)
        { Items.Enqueue(item); Saved.Writer.TryWrite(item); return Task.FromResult(item); }
    }
    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; private set; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Current = settings;
        public void Update(Action<AppSettings> mutate) => mutate(Current);
    }
    private sealed class FakeImages : IImageStorageService
    {
        public int SaveCalls { get; private set; }
        public Task<ImageSaveResult> SaveImageAsync(byte[] imageBytes, string extension, CancellationToken ct)
        { SaveCalls++; return Task.FromResult(new ImageSaveResult("image.png", "thumb.png")); }
        public Task<string?> GenerateThumbnailAsync(string originalImagePath, CancellationToken ct) => Task.FromResult<string?>("thumb.png");
    }
}
