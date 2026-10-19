using ClipboardManager.Domain;

namespace ClipboardManager.Contracts;

public interface IWorkspaceRepository
{
    Task<IReadOnlyList<ClipboardItem>> QueryAsync(ClipboardQuery query, int offset, int limit, CancellationToken ct);
    Task<IReadOnlyList<Pinboard>> GetPinboardsAsync(CancellationToken ct);
    Task SavePinboardAsync(Pinboard board, string? previousName, CancellationToken ct);
    Task DeletePinboardAsync(string name, CancellationToken ct);
    Task ReorderPinboardsAsync(IReadOnlyList<string> names, CancellationToken ct);
    Task UpdateContentAsync(ClipboardItem item, string searchContent, CancellationToken ct);
    Task ApplyBatchAsync(IReadOnlyList<long> ids, string action, string? group, CancellationToken ct);
}

public interface IWorkspaceService : IWorkspaceRepository
{
    Task SaveTextAsync(ClipboardItem? original, string title, string text, CancellationToken ct);
}

public interface IImageTextService
{
    Task<string> ExtractAsync(string path, CancellationToken ct, string? languageTag = null, bool enhanceSmallText = false);
}
