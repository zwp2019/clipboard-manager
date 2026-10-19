using System.Text;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;

namespace ClipboardManager.Application;

public sealed class WorkspaceService(IWorkspaceRepository repository, IClipboardItemRepository items, IClipboardHashService hash) : IWorkspaceService
{
    public Task<IReadOnlyList<ClipboardItem>> QueryAsync(ClipboardQuery query, int offset, int limit, CancellationToken ct)
        => repository.QueryAsync(query, offset, limit, ct);
    public Task<IReadOnlyList<Pinboard>> GetPinboardsAsync(CancellationToken ct) => repository.GetPinboardsAsync(ct);
    public Task SavePinboardAsync(Pinboard board, string? previousName, CancellationToken ct)
    {
        board.Name = board.Name.Trim();
        if (board.Name.Length is 0 or > 60) throw new ArgumentException("收藏板名称需要 1–60 个字符。");
        if (board.Color.Length != 7 || board.Color[0] != '#' || !int.TryParse(board.Color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _))
            throw new ArgumentException("请选择有效的颜色。");
        return repository.SavePinboardAsync(board, previousName, ct);
    }
    public Task DeletePinboardAsync(string name, CancellationToken ct) => repository.DeletePinboardAsync(name, ct);
    public Task ReorderPinboardsAsync(IReadOnlyList<string> names, CancellationToken ct) => repository.ReorderPinboardsAsync(names, ct);
    public Task UpdateContentAsync(ClipboardItem item, string searchContent, CancellationToken ct) => repository.UpdateContentAsync(item, searchContent, ct);
    public Task ApplyBatchAsync(IReadOnlyList<long> ids, string action, string? group, CancellationToken ct) => repository.ApplyBatchAsync(ids, action, group, ct);

    public async Task SaveTextAsync(ClipboardItem? original, string title, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) && original is null) throw new ArgumentException("请先填写内容。");
        var item = original is null ? new ClipboardItem { SourceApplication = "ClipboardManager", IsFavorite = true } :
            await items.GetByIdAsync(original.Id, ct) ?? throw new InvalidOperationException("条目已被删除，请刷新。");
        item.Title = title.Trim();
        if (item.Type is not (ClipboardType.Image or ClipboardType.File))
        {
            if (item.Text != text)
            {
                item.Type = ClipboardType.Text;
                item.Html = null;
                item.Rtf = null;
                item.Text = text;
                item.Hash = hash.ComputeHash(new ClipboardData { Type = ClipboardType.Text, Text = text });
                item.Size = Encoding.UTF8.GetByteCount(text);
            }
        }
        item.UpdatedAt = DateTime.Now;
        var content = string.Join('\n', item.Title, item.GetPreviewText(), item.OcrText);
        if (original is null)
        {
            item.Id = await items.InsertAsync(item, ct);
            await items.UpsertSearchContentAsync(item.Id, content, ct);
        }
        else await repository.UpdateContentAsync(item, content, ct);
    }
}
