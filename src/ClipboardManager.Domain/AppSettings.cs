namespace ClipboardManager.Domain;

/// <summary>
/// 应用主题（规范 §47）。
/// </summary>
public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>
/// 应用设置（规范 §46）。
/// </summary>
public sealed class AppSettings
{
    /// <summary>全局热键（默认 Ctrl+Shift+V，规范 §28）。</summary>
    public string Hotkey { get; set; } = "Ctrl+Shift+V";

    /// <summary>是否开机自启（规范 §32）。</summary>
    public bool AutoStart { get; set; }

    /// <summary>主题（规范 §47）。</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>历史保留天数，0 = 永久（规范 §40）。</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>是否启用去重（规范 §15）。</summary>
    public bool EnableDeduplication { get; set; } = true;

    /// <summary>历史最大条数，超出自动清理最旧（规范 §46）。</summary>
    public int MaxHistoryItems { get; set; } = 50000;

    /// <summary>是否对图片压缩（规范 §21）。</summary>
    public bool ImageCompression { get; set; } = true;

    /// <summary>排除的应用（来源应用名），其复制内容不入库（规范 §39）。</summary>
    public List<string> ExcludedApplications { get; set; } = new();

    /// <summary>是否暂停监控（规范 §31 托盘菜单）。</summary>
    public bool PauseMonitoring { get; set; }

    public DateTime? PauseUntil { get; set; }

    public bool AlwaysPastePlainText { get; set; }

    /// <summary>托盘菜单退出时是否需要确认。</summary>
    public bool ConfirmOnExit { get; set; } = true;

    public AppSettings Clone()
    {
        var clone = (AppSettings)MemberwiseClone();
        clone.ExcludedApplications = new List<string>(ExcludedApplications);
        return clone;
    }
}
