using ClipboardManager.Contracts;
using ClipboardManager.Domain;

namespace ClipboardManager.Application;

/// <summary>
/// 全文搜索服务：委托仓储完成 FTS5 查询，支持取消（规范 §27）。
/// 防抖与 UI 线程切换由 ViewModel 层负责。
/// </summary>
public sealed class SearchService : ISearchService
{
    private readonly IClipboardItemRepository _repository;

    public SearchService(IClipboardItemRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<ClipboardItem>> SearchAsync(string query, int offset, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyList<ClipboardItem>>(Array.Empty<ClipboardItem>());
        }

        return _repository.SearchAsync(query.Trim(), offset, limit, ct);
    }
}
