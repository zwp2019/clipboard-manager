using System.Collections.ObjectModel;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using ClipboardManager.App.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipboardManager.App.ViewModels;

/// <summary>
/// Launcher 主窗口 ViewModel：搜索（Debounce + 取消旧查询）、历史、收藏、快速粘贴（规范 §27、§29）。
/// </summary>
public partial class LauncherViewModel : ObservableObject, IDisposable
{
    private const int PageSize = 80;

    private readonly IHistoryService _history;
    private readonly ISearchService _search;
    private readonly IQuickPasteService _quickPaste;
    private readonly IClipboardWriter _writer;
    private readonly IDispatcher _dispatcher;
    private readonly IDebouncer _debouncer;
    private readonly LruImageCache _imageCache;

    private CancellationTokenSource? _searchCts;
    private string _view = "history";
    private nint _pasteTargetWindow;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private string statusText = "准备就绪";

    [ObservableProperty]
    private ObservableCollection<ClipboardItemViewModel> items = new();

    [ObservableProperty]
    private ClipboardItemViewModel? selectedItem;

    private IReadOnlyList<ClipboardItem> _lastResults = new List<ClipboardItem>();

    public LauncherViewModel(
        IHistoryService history,
        ISearchService search,
        IQuickPasteService quickPaste,
        IClipboardWriter writer,
        IDispatcher dispatcher,
        IDebouncer debouncer,
        LruImageCache imageCache)
    {
        _history = history;
        _search = search;
        _quickPaste = quickPaste;
        _writer = writer;
        _dispatcher = dispatcher;
        _debouncer = debouncer;
        _imageCache = imageCache;
    }

    /// <summary>
    /// 记录打开 Launcher 前的活动窗口，供快速粘贴恢复（规范 §30）。
    /// </summary>
    public void SetPasteTarget(nint windowHandle)
        => _pasteTargetWindow = windowHandle;

    /// <summary>
    /// 搜索框文本变化：300ms 防抖 + 取消旧查询（规范 §27）。
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        _debouncer.Debounce(() => _ = ExecuteSearchAsync(value, ct), 300);
    }

    public async Task RefreshAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        await ExecuteSearchAsync(SearchText, ct);
    }

    public async Task ShowFavoritesAsync()
    {
        _view = "favorites";
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        await LoadAsync(() => _history.GetFavoritesAsync(0, PageSize, ct), "收藏", ct);
    }

    public async Task ShowHistoryAsync()
    {
        _view = "history";
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        await ExecuteSearchAsync(string.Empty, ct);
    }

    /// <summary>
    /// 立即按指定关键词执行搜索（回车触发，跳过输入防抖）。不传则用当前绑定关键词。
    /// </summary>
    public async Task SearchNowAsync(string? query = null)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        await ExecuteSearchAsync(query ?? SearchText?.Trim() ?? string.Empty, ct);
    }

    private async Task ExecuteSearchAsync(string query, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<ClipboardItem> results;
            string label;

            if (string.IsNullOrWhiteSpace(query))
            {
                _view = "history";
                results = await _history.GetRecentAsync(0, PageSize, ct);
                label = "最近";
            }
            else
            {
                _view = "search";
                results = await _search.SearchAsync(query, 0, PageSize, ct);
                label = $"搜索 “{query}”";
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            await ApplyResultsAsync(results, label);
        }
        catch (OperationCanceledException)
        {
            // 旧查询被取消，忽略（规范 §27）。
        }
        catch (Exception)
        {
            await _dispatcher.PostAsync(
                () =>
                {
                    IsSearching = false;
                    StatusText = "加载失败";
                });
        }
    }

    private async Task LoadAsync(
        Func<Task<IReadOnlyList<ClipboardItem>>> loader,
        string label,
        CancellationToken ct)
    {
        try
        {
            var results = await loader();
            if (ct.IsCancellationRequested)
            {
                return;
            }

            await ApplyResultsAsync(results, label);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            await _dispatcher.PostAsync(() => StatusText = "加载失败");
        }
    }

    private async Task ApplyResultsAsync(IReadOnlyList<ClipboardItem> results, string label)
    {
        _lastResults = results;
        await _dispatcher.PostAsync(
            () =>
            {
                StatusText = $"{label} · {results.Count} 条";
                IsSearching = false;

                // 结果集与当前显示一致时复用现有 VM，避免整体重建导致重新实例化与布局闪烁。
                if (Items.Count == results.Count
                    && Items.Select(i => i.Item.Id).SequenceEqual(results.Select(r => r.Id)))
                {
                    SelectedItem = Items.Count > 0 ? Items[0] : null;
                    return;
                }

                Items = new ObservableCollection<ClipboardItemViewModel>(
                    results.Select(item => new ClipboardItemViewModel(item, _imageCache)));
                // 列表为空时必须清空选中，避免操作作用于不可见条目。
                SelectedItem = Items.Count > 0 ? Items[0] : null;
            });
    }

    /// <summary>
    /// 按类型显示最近条目（Dock 顶部标签栏使用）：始终查库，
    /// 避免基于上一个视图的内存结果集过滤导致数据错乱。
    /// </summary>
    public Task ShowTypeAsync(ClipboardType type)
    {
        _view = "history";
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        var label = type switch
        {
            ClipboardType.Url => "链接",
            ClipboardType.Code => "代码",
            ClipboardType.Image => "图片",
            _ => "条目",
        };
        return LoadAsync(() => _history.GetByTypeAsync(type, 0, PageSize, ct), label, ct);
    }

    /// <summary>
    /// 显示指定自定义分组的内容（标签栏动态标签，始终查库）。
    /// </summary>
    public Task ShowGroupAsync(string group)
    {
        _view = "group";
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        return LoadAsync(() => _history.GetByGroupAsync(group, 0, PageSize, ct), group, ct);
    }

    public async Task PasteSelectedAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        await _quickPaste.PasteAsync(SelectedItem.Item, _pasteTargetWindow, CancellationToken.None);
    }

    /// <summary>
    /// 复制选中项到剪贴板（Ctrl+C）：只写入，不激活窗口、不发送粘贴（规范 §30）。
    /// </summary>
    public async Task CopySelectedAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var operationId = Guid.NewGuid().ToString("N");
        await _writer.WriteAsync(SelectedItem.Item, operationId, CancellationToken.None);
        StatusText = $"已复制 · {SelectedItem.CardTitle}";
    }

    /// <summary>
    /// 键盘左右方向键在卡片间移动选中项（规范 §29）。offset 为 -1 左移、+1 右移，
    /// 到达边界时保持不动；无选中项时定位到首项。
    /// </summary>
    public ClipboardItemViewModel? MoveSelection(int offset)
    {
        if (Items.Count == 0)
        {
            return null;
        }

        var currentIndex = SelectedItem is null ? -1 : Items.IndexOf(SelectedItem);
        var nextIndex = Math.Clamp(currentIndex + offset, 0, Items.Count - 1);
        SelectedItem = Items[nextIndex];
        return SelectedItem;
    }

    /// <summary>全部现有分组名称（分组对话框使用）。</summary>
    public Task<IReadOnlyList<string>> GetGroupNamesAsync()
        => _history.GetGroupNamesAsync(CancellationToken.None);

    /// <summary>设置/移除选中项的分组。</summary>
    public Task SetGroupAsync(ClipboardItemViewModel item, string? group)
        => item.SetGroupAsync(_history, group);

    public async Task ToggleFavoriteAsync(ClipboardItemViewModel item)
    {
        if (item is null)
        {
            return;
        }

        await item.ToggleFavoriteAsync(_history);
        if (_view == "favorites" && !item.IsFavorite)
        {
            RemoveFromView(item, "已取消收藏");
        }
    }

    /// <summary>
    /// 从当前视图移除条目：同步选中项与计数（修复删除/取消收藏后操作残留于不可见条目的缺陷）。
    /// </summary>
    private void RemoveFromView(ClipboardItemViewModel item, string reason)
    {
        var wasSelected = ReferenceEquals(SelectedItem, item);
        var index = Items.IndexOf(item);
        Items.Remove(item);

        if (wasSelected)
        {
            // 就近选择同位置的下一个条目，保持键盘流连续。
            SelectedItem = index < Items.Count ? Items[index] : Items.LastOrDefault();
        }

        StatusText = $"{reason} · {Items.Count} 条";
    }

    public async Task DeleteAsync(ClipboardItemViewModel item)
    {
        if (item is null)
        {
            return;
        }

        await _history.DeleteAsync(item.Item.Id, CancellationToken.None);
        RemoveFromView(item, "已删除 1 条");
    }

    public void Dispose()
    {
        _debouncer.Dispose();
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
