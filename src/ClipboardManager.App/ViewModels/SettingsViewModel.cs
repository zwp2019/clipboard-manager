using System.Collections.ObjectModel;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipboardManager.App.ViewModels;

/// <summary>
/// 设置页 ViewModel（规范 §46）。
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IAutoStartService _autoStartService;
    private readonly ThemeService _themeService;

    [ObservableProperty]
    private string hotkey = "Ctrl+Shift+V";

    [ObservableProperty]
    private string hotkeyStatus = string.Empty;

    [ObservableProperty]
    private bool autoStart;

    [ObservableProperty]
    private int themeIndex;

    [ObservableProperty]
    private int retentionDays = 30;

    [ObservableProperty]
    private bool enableDeduplication = true;

    [ObservableProperty]
    private int maxHistoryItems = 50000;

    [ObservableProperty]
    private bool imageCompression = true;

    [ObservableProperty]
    private bool pauseMonitoring;

    [ObservableProperty]
    private ObservableCollection<string> excludedApplications = new();

    public SettingsViewModel(
        ISettingsService settingsService,
        IHotkeyService hotkeyService,
        IAutoStartService autoStartService,
        ThemeService themeService)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _autoStartService = autoStartService;
        _themeService = themeService;
    }

    public int[] RetentionOptions { get; } = { 1, 7, 30, 90, 0 };

    public void Load()
    {
        var settings = _settingsService.Current;
        Hotkey = settings.Hotkey;
        AutoStart = settings.AutoStart;
        ThemeIndex = (int)settings.Theme;
        RetentionDays = settings.RetentionDays;
        EnableDeduplication = settings.EnableDeduplication;
        MaxHistoryItems = settings.MaxHistoryItems;
        ImageCompression = settings.ImageCompression;
        PauseMonitoring = settings.PauseMonitoring;
        ExcludedApplications = new ObservableCollection<string>(settings.ExcludedApplications);
        HotkeyStatus = _hotkeyService.IsRegistered ? "热键已注册" : "热键未注册（点击保存后重试）";
    }

    public void Save()
    {
        var settings = _settingsService.Current;
        settings.Hotkey = Hotkey;
        settings.AutoStart = AutoStart;
        settings.Theme = (AppTheme)ThemeIndex;
        settings.RetentionDays = RetentionDays;
        settings.EnableDeduplication = EnableDeduplication;
        settings.MaxHistoryItems = MaxHistoryItems;
        settings.ImageCompression = ImageCompression;
        settings.PauseMonitoring = PauseMonitoring;
        settings.ExcludedApplications = ExcludedApplications.ToList();
        _settingsService.Save(settings);

        ApplySideEffects(settings);
    }

    /// <summary>
    /// 热键输入框捕获到按键组合时实时校验（规范 §28）。
    /// </summary>
    public void TryApplyHotkey(string candidate)
    {
        if (_hotkeyService.TryParse(candidate, out var error))
        {
            Hotkey = candidate;
            HotkeyStatus = string.Empty;
        }
        else
        {
            HotkeyStatus = $"无效热键：{error}";
        }
    }

    public void AddExcludedApplication(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return;
        }

        if (!ExcludedApplications.Contains(appName, StringComparer.OrdinalIgnoreCase))
        {
            ExcludedApplications.Add(appName);
        }
    }

    public void RemoveExcludedApplication(string appName)
        => ExcludedApplications.Remove(appName);

    private void ApplySideEffects(AppSettings settings)
    {
        // 主题立即生效（规范 §47）
        _themeService.Apply(settings.Theme);

        // 开机自启：仅在用户显式修改时写入注册表（规范 §32）
        if (AutoStart != _autoStartService.IsEnabled())
        {
            if (AutoStart)
            {
                _autoStartService.Enable();
            }
            else
            {
                _autoStartService.Disable();
            }
        }

        // 热键重新注册（冲突检测，规范 §28）
        if (!string.IsNullOrWhiteSpace(Hotkey))
        {
            var ok = _hotkeyService.Register(Hotkey);
            HotkeyStatus = ok ? "热键已注册" : "热键注册失败（可能被其他程序占用）";
        }
    }
}
