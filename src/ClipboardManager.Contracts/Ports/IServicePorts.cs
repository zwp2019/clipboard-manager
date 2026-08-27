using ClipboardManager.Domain;

namespace ClipboardManager.Contracts;

/// <summary>
/// 全文搜索服务：FTS5，支持取消（规范 §27）。
/// </summary>
public interface ISearchService
{
    Task<IReadOnlyList<ClipboardItem>> SearchAsync(string query, int offset, int limit, CancellationToken ct);
}

/// <summary>
/// 历史服务：最近列表、收藏、删除、清空（规范 §7）。
/// </summary>
public interface IHistoryService
{
    Task<IReadOnlyList<ClipboardItem>> GetRecentAsync(int offset, int limit, CancellationToken ct);

    Task<IReadOnlyList<ClipboardItem>> GetFavoritesAsync(int offset, int limit, CancellationToken ct);

    /// <summary>按类型查询最近条目（Dock 标签切换）。</summary>
    Task<IReadOnlyList<ClipboardItem>> GetByTypeAsync(ClipboardType type, int offset, int limit, CancellationToken ct);

    /// <summary>查询指定自定义分组的条目（标签栏动态标签，规范 §33）。</summary>
    Task<IReadOnlyList<ClipboardItem>> GetByGroupAsync(string group, int offset, int limit, CancellationToken ct);

    Task<ClipboardItem?> GetByIdAsync(long id, CancellationToken ct);

    Task SetFavoriteAsync(long id, bool favorite, CancellationToken ct);

    /// <summary>设置/移除条目的自定义分组（规范 §33）。</summary>
    Task SetGroupAsync(long id, string? group, CancellationToken ct);

    /// <summary>全部现有分组名称（去重）。</summary>
    Task<IReadOnlyList<string>> GetGroupNamesAsync(CancellationToken ct);

    Task DeleteAsync(long id, CancellationToken ct);

    Task ClearAsync(CancellationToken ct);

    Task<int> CountAsync(CancellationToken ct);
}

/// <summary>
/// 历史过期清理服务：后台任务（规范 §40）。
/// </summary>
public interface IHistoryCleanupService
{
    Task StartAsync(CancellationToken ct);

    Task CleanupOnceAsync(CancellationToken ct);
}

/// <summary>
/// 备份/恢复服务（规范 §46）。
/// </summary>
public interface IBackupService
{
    Task<string> CreateBackupAsync(string destinationDirectory, CancellationToken ct);

    Task RestoreAsync(string backupFilePath, CancellationToken ct);
}
