using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Application;

/// <summary>
/// 剪贴板处理管道（规范 §11）：Monitor → Reader → Normalizer → Hash → Dedup → Storage。
/// 负责高频事件合并、排除应用、异常隔离；单个条目失败不影响整个引擎（规范 §43、§45）。
/// </summary>
public sealed class ClipboardPipelineService : IDisposable
{
    private readonly IClipboardMonitor _monitor;
    private readonly IClipboardReader _reader;
    private readonly IClipboardNormalizer _normalizer;
    private readonly IClipboardHashService _hashService;
    private readonly IClipboardStorageService _storage;
    private readonly ClipboardDeduplicationService _deduplication;
    private readonly ISettingsService _settingsService;
    private readonly IInternalClipboardGuard _guard;
    private readonly ILogger<ClipboardPipelineService> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private bool _started;

    public ClipboardPipelineService(
        IClipboardMonitor monitor,
        IClipboardReader reader,
        IClipboardNormalizer normalizer,
        IClipboardHashService hashService,
        IClipboardStorageService storage,
        ClipboardDeduplicationService deduplication,
        ISettingsService settingsService,
        IInternalClipboardGuard guard,
        ILogger<ClipboardPipelineService> logger)
    {
        _monitor = monitor;
        _reader = reader;
        _normalizer = normalizer;
        _hashService = hashService;
        _storage = storage;
        _deduplication = deduplication;
        _settingsService = settingsService;
        _guard = guard;
        _logger = logger;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _monitor.ClipboardChanged += OnClipboardChanged;
        _monitor.Start();
        _started = true;
        _logger.LogInformation("Clipboard pipeline started");
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _monitor.ClipboardChanged -= OnClipboardChanged;
        _monitor.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _started = false;
        _logger.LogInformation("Clipboard pipeline stopped");
    }

    private void OnClipboardChanged(object? sender, ClipboardChangedEventArgs e)
    {
        // 防死循环：自身写入剪贴板产生的事件直接忽略（规范 §13）。
        if (_guard.IsOwnWrite)
        {
            _logger.LogTrace("Ignoring own clipboard write");
            return;
        }

        _ = Task.Run(() => ProcessAsync(e, _cts?.Token ?? CancellationToken.None));
    }

    private async Task ProcessAsync(ClipboardChangedEventArgs args, CancellationToken ct)
    {
        // 串行化处理，避免高频复制导致并发写库。
        await _gate.WaitAsync(ct);
        try
        {
            // 暂停监控
            if (_settingsService.Current.PauseMonitoring)
            {
                return;
            }

            var data = await _reader.ReadAsync(ct);
            if (data is null || data.Size <= 0)
            {
                return;
            }

            // 来源应用：监听器已在前台窗口切换前捕获（排除本进程自身）。
            if (string.IsNullOrEmpty(data.SourceApplication)
                && !string.IsNullOrEmpty(args.SourceApplication)
                && !string.Equals(
                    args.SourceApplication,
                    Environment.ProcessPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                data.SourceApplication = args.SourceApplication;
            }

            // 敏感应用排除（规范 §39）：来源应用进程名命中排除列表则不入库。
            // SourceApplication 为完整路径，按文件名（去扩展名）精确比较，与设置页“添加当前应用”一致。
            var sourceProcessName = Path.GetFileNameWithoutExtension(data.SourceApplication);
            if (!string.IsNullOrEmpty(sourceProcessName)
                && _settingsService.Current.ExcludedApplications.Any(
                    x => x.Equals(sourceProcessName, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogDebug("Skipping clipboard from excluded app {App}", sourceProcessName);
                return;
            }

            var hash = _hashService.ComputeHash(data);

            // 高频去重窗口（规范 §15）
            if (_deduplication.IsDuplicateWithinWindow(hash, DateTime.UtcNow))
            {
                _logger.LogTrace("Duplicate clipboard content ignored, hash={Hash}", ShortHash(hash));
                return;
            }

            var item = _normalizer.Normalize(data, hash);
            await _storage.SaveAsync(item, data.ImageBytes, data.ImageExtension, ct);
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            // 单个条目失败不能影响整个引擎（规范 §45）。
            _logger.LogError(ex, "Failed to process clipboard change (operation {OperationId})", args.OperationId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string ShortHash(string hash)
        => hash.Length <= 12 ? hash : hash[..12];

    public void Dispose()
    {
        Stop();
        _gate.Dispose();
        _deduplication.Reset();
    }
}
