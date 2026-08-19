using ClipboardManager.Contracts;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Application;

/// <summary>
/// 历史过期后台清理服务（规范 §40）。
/// 启动后每 60 分钟执行一次清理；首次清理延迟 2 分钟，避免影响启动速度（规范 §33）。
/// </summary>
public sealed class HistoryCleanupService : IHistoryCleanupService
{
    private readonly IClipboardItemRepository _repository;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<HistoryCleanupService> _logger;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public HistoryCleanupService(
        IClipboardItemRepository repository,
        ISettingsService settingsService,
        ILogger<HistoryCleanupService> logger)
    {
        _repository = repository;
        _settingsService = settingsService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => CleanupLoopAsync(_cts.Token));
        _logger.LogInformation("History cleanup service started");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
                // 正常停止
            }
        }
    }

    private async Task CleanupLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), ct);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await CleanupOnceAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 清理失败不影响运行，下一轮重试（规范 §43、§45）。
                    _logger.LogError(ex, "History cleanup pass failed");
                }

                await Task.Delay(TimeSpan.FromMinutes(60), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // 停止
        }
    }

    public async Task CleanupOnceAsync(CancellationToken ct)
    {
        var settings = _settingsService.Current;

        // 1. 按保留天数清理
        if (settings.RetentionDays > 0)
        {
            var cutoff = DateTime.Now.AddDays(-settings.RetentionDays);
            var removed = await _repository.DeleteOlderThanAsync(cutoff, ct);
            if (removed > 0)
            {
                _logger.LogInformation("Cleaned {Count} expired clipboard items", removed);
            }
        }

        // 2. 历史上限兜底
        if (settings.MaxHistoryItems > 0)
        {
            await _repository.TrimToLimitAsync(settings.MaxHistoryItems, ct);
        }
    }
}
