using ClipboardManager.Contracts;
using ClipboardManager.Domain;

namespace ClipboardManager.Application;

/// <summary>
/// 历史管理服务：最近列表、收藏、删除、清空（规范 §7）。
/// </summary>
public sealed class HistoryService : IHistoryService
{
    private readonly IClipboardItemRepository _repository;

    public HistoryService(IClipboardItemRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<ClipboardItem>> GetRecentAsync(int offset, int limit, CancellationToken ct)
        => _repository.GetRecentAsync(offset, limit, ct);

    public Task<IReadOnlyList<ClipboardItem>> GetFavoritesAsync(int offset, int limit, CancellationToken ct)
        => _repository.GetFavoritesAsync(offset, limit, ct);

    public Task<IReadOnlyList<ClipboardItem>> GetByTypeAsync(ClipboardType type, int offset, int limit, CancellationToken ct)
        => _repository.GetByTypeAsync(type, offset, limit, ct);

    public Task<IReadOnlyList<ClipboardItem>> GetByGroupAsync(string group, int offset, int limit, CancellationToken ct)
        => _repository.GetByGroupAsync(group, offset, limit, ct);

    public Task<ClipboardItem?> GetByIdAsync(long id, CancellationToken ct)
        => _repository.GetByIdAsync(id, ct);

    public Task SetFavoriteAsync(long id, bool favorite, CancellationToken ct)
        => _repository.SetFavoriteAsync(id, favorite, ct);

    public Task SetGroupAsync(long id, string? group, CancellationToken ct)
        => _repository.SetGroupAsync(id, group, ct);

    public Task<IReadOnlyList<string>> GetGroupNamesAsync(CancellationToken ct)
        => _repository.GetGroupNamesAsync(ct);

    public Task DeleteAsync(long id, CancellationToken ct)
        => _repository.DeleteAsync(id, ct);

    public Task ClearAsync(CancellationToken ct)
        => _repository.ClearAsync(ct);

    public Task<int> CountAsync(CancellationToken ct)
        => _repository.CountAsync(ct);
}
