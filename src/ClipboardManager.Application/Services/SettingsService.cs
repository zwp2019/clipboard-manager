using System.Text.Json;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Application;

/// <summary>
/// 设置服务：强类型加载/保存/更新（规范 §46）。以 JSON 存储于 settings 表。
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private const string SettingsKey = "app_settings";

    private readonly ISettingsRepository _repository;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AppSettings _current = new();

    public AppSettings Current => _current;

    public SettingsService(ISettingsRepository repository, ILogger<SettingsService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public AppSettings Load()
    {
        _gate.Wait();
        try
        {
            var raw = _repository.GetAsync(SettingsKey, CancellationToken.None).GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(raw))
            {
                _current = new AppSettings();
                Persist(_current);
                return _current;
            }

            try
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(raw) ?? new AppSettings();
                _current = loaded;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize settings, falling back to defaults");
                _current = new AppSettings();
            }

            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Save(AppSettings settings)
    {
        _gate.Wait();
        try
        {
            _current = settings.Clone();
            Persist(_current);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Update(Action<AppSettings> mutate)
    {
        _gate.Wait();
        try
        {
            var next = _current.Clone();
            mutate(next);
            _current = next;
            Persist(_current);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Persist(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings);
            _repository.SetAsync(SettingsKey, json, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist settings");
        }
    }
}
