using System.Text.Json;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Application;

/// <summary>
/// 存储编排服务：图片落盘、去重、入库、搜索索引、历史上限控制（规范 §11、§15、§20）。
/// </summary>
public sealed partial class ClipboardStorageService : IClipboardStorageService
{
    private readonly IClipboardItemRepository _repository;
    private readonly IImageStorageService _imageStorage;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ClipboardStorageService> _logger;

    public ClipboardStorageService(
        IClipboardItemRepository repository,
        IImageStorageService imageStorage,
        ISettingsService settingsService,
        ILogger<ClipboardStorageService> logger)
    {
        _repository = repository;
        _imageStorage = imageStorage;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<ClipboardItem> SaveAsync(
        ClipboardItem item,
        byte[]? imageBytes,
        string? imageExtension,
        CancellationToken ct)
    {
        // 1. 图片先落盘（后台线程处理，规范 §21）
        if (item.Type == ClipboardType.Image && imageBytes is { Length: > 0 })
        {
            var result = await _imageStorage.SaveImageAsync(imageBytes, imageExtension ?? "png", ct);
            item.ImagePath = result.ImagePath;
            item.ThumbnailPath = result.ThumbnailPath;
        }

        // 2. 去重：命中则仅刷新时间戳移动到最前
        var existing = await _repository.FindByHashAsync(item.Hash, ct);
        if (existing is not null && _settingsService.Current.EnableDeduplication)
        {
            await _repository.TouchAsync(existing.Id, ct);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                Log.DedupHit(_logger, item.Hash[..Math.Min(12, item.Hash.Length)], existing.Id);
            }

            return existing;
        }

        // 3. 入库
        item.UpdatedAt = DateTime.Now;
        var id = await _repository.InsertAsync(item, ct);
        item.Id = id;

        // 4. 全文搜索索引（FTS5）
        await IndexForSearchAsync(item, ct);

        // 5. 历史上限控制（后台机会式，不影响写入主路径）
        try
        {
            var max = _settingsService.Current.MaxHistoryItems;
            if (max > 0)
            {
                await _repository.TrimToLimitAsync(max, ct);
            }
        }
        catch (Exception ex)
        {
            Log.TrimFailed(_logger, ex);
        }

        return item;
    }

    private async Task IndexForSearchAsync(ClipboardItem item, CancellationToken ct)
    {
        string? content = item.GetPreviewText();
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        await _repository.UpsertSearchContentAsync(item.Id, content, ct);
    }

    internal static string SerializeFilePaths(IReadOnlyList<string> paths)
    {
        return JsonSerializer.Serialize(paths);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 0, Level = LogLevel.Debug, Message = "Dedup hit for hash {Hash}, touched item {Id}")]
        public static partial void DedupHit(ILogger logger, string hash, long id);

        [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Trim to max history failed")]
        public static partial void TrimFailed(ILogger logger, Exception exception);
    }
}
