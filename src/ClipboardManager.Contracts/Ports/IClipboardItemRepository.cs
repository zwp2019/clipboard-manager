using ClipboardManager.Domain;

namespace ClipboardManager.Contracts;

/// <summary>
/// 剪贴板条目仓储：SQLite CRUD、分页、收藏、搜索（规范 §16-§19）。
/// </summary>
public interface IClipboardItemRepository
{
    Task<long> InsertAsync(ClipboardItem item, CancellationToken ct);

    Task<ClipboardItem?> GetByIdAsync(long id, CancellationToken ct);

    Task<ClipboardItem?> FindByHashAsync(string hash, CancellationToken ct);

    Task<IReadOnlyList<ClipboardItem>> GetRecentAsync(int offset, int limit, CancellationToken ct);

    Task<IReadOnlyList<ClipboardItem>> GetFavoritesAsync(int offset, int limit, CancellationToken ct);

    /// <summary>按类型查询最近条目（Dock 标签切换，规范 §34）。</summary>
    Task<IReadOnlyList<ClipboardItem>> GetByTypeAsync(ClipboardType type, int offset, int limit, CancellationToken ct);

    /// <summary>查询指定自定义分组的条目（标签栏动态标签，规范 §33）。</summary>
    Task<IReadOnlyList<ClipboardItem>> GetByGroupAsync(string group, int offset, int limit, CancellationToken ct);

    Task<IReadOnlyList<ClipboardItem>> SearchAsync(string query, int offset, int limit, CancellationToken ct);

    Task<int> CountAsync(CancellationToken ct);

    Task SetFavoriteAsync(long id, bool favorite, CancellationToken ct);

    /// <summary>设置/移除条目的自定义分组（null 表示移出，规范 §33）。</summary>
    Task SetGroupAsync(long id, string? group, CancellationToken ct);

    /// <summary>全部现有分组名称（去重）。</summary>
    Task<IReadOnlyList<string>> GetGroupNamesAsync(CancellationToken ct);

    /// <summary>去重命中：仅刷新时间戳使其移动到最前（规范 §15）。</summary>
    Task TouchAsync(long id, CancellationToken ct);

    Task DeleteAsync(long id, CancellationToken ct);

    Task ClearAsync(CancellationToken ct);

    Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct);

    /// <summary>删除最旧的超过上限的非收藏条目（规范 §46）。</summary>
    Task<int> TrimToLimitAsync(int maxItems, CancellationToken ct);

    /// <summary>写入/更新全文搜索内容（FTS5，规范 §18）。</summary>
    Task UpsertSearchContentAsync(long id, string content, CancellationToken ct);
}

/// <summary>
/// 键值设置仓储。
/// </summary>
public interface ISettingsRepository
{
    Task<string?> GetAsync(string key, CancellationToken ct);

    Task SetAsync(string key, string value, CancellationToken ct);
}
