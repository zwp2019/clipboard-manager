using System.Collections.ObjectModel;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using ClipboardManager.App.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipboardManager.App.ViewModels;

/// <summary>
/// Launcher 主窗口 ViewModel：搜索（Debounce + 取消旧查询）、历史、收藏、快速粘贴（规范 §27、§29）。
/// </summary>
public partial class LauncherViewModel : ObservableObject
{
    private const int PageSize = 80;

    private readonly IHistoryService _history;
    private readonly ISearchService _search;
    private readonly IQuickPasteService _quickPaste;
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

    public LauncherViewModel(
        IHistoryService history,
        ISearchService search,
        IQuickPasteService quickPaste,
        IDispatcher dispatcher,
        IDebouncer debouncer,
        LruImageCache imageCache)
    {
        _history = history;
        _search = search;
        _quickPaste = quickPaste;
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
        await _dispatcher.PostAsync(
            () =>
            {
                Items = new ObservableCollection<ClipboardItemViewModel>(
                    results.Select(item => new ClipboardItemViewModel(item, _imageCache)));
                StatusText = $"{label} · {results.Count} 条";
                IsSearching = false;
                if (Items.Count > 0)
                {
                    SelectedItem = Items[0];
                }
            });
    }

    public async Task PasteSelectedAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        await _quickPaste.PasteAsync(SelectedItem.Item, _pasteTargetWindow, CancellationToken.None);
    }

    public async Task ToggleFavoriteAsync(ClipboardItemViewModel item)
    {
        if (item is null)
        {
            return;
        }

        await item.ToggleFavoriteAsync(_history);
        if (_view == "favorites" && !item.IsFavorite)
        {
            Items.Remove(item);
        }
    }

    public async Task DeleteAsync(ClipboardItemViewModel item)
    {
        if (item is null)
        {
            return;
        }

        await _history.DeleteAsync(item.Item.Id, CancellationToken.None);
        Items.Remove(item);
        StatusText = $"已删除 1 条 · {Items.Count} 条";
    }

    public void Dispose()
    {
        _debouncer.Dispose();
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
}
