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

    Task<ClipboardItem?> GetByIdAsync(long id, CancellationToken ct);

    Task SetFavoriteAsync(long id, bool favorite, CancellationToken ct);

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
