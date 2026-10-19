using System.Collections.ObjectModel;
using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace ClipboardManager.App.ViewModels;

public partial class LauncherViewModel : ObservableObject, IDisposable
{
    private const int PageSize = 80;
    private readonly IWorkspaceService _workspace;
    private readonly IHistoryService _history;
    private readonly IQuickPasteService _paste;
    private readonly IClipboardWriter _writer;
    private readonly ISettingsService _settings;
    private readonly IImageTextService _ocr;
    private readonly IDispatcher _dispatcher;
    private readonly IDebouncer _debouncer;
    private readonly CardAssetLoader _assets;
    private readonly ILogger<LauncherViewModel> _logger;
    private CancellationTokenSource? _queryCts;
    private ClipboardQuery _scope = new();
    private nint _target;
    private readonly List<ClipboardItem> _queue = new();
    private bool _pasteBusy;
    private int _searchVersion;
    private int _nextOffset;
    public event Action<string, bool>? CopyFeedback;

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private bool isSearching;
    [ObservableProperty] private string statusText = "你的剪贴板，井然有序";
    [ObservableProperty] private string activeList = "全部历史";
    [ObservableProperty] private bool hasMore;
    [ObservableProperty] private bool isEmpty = true;
    [ObservableProperty] private string queueLabel = "粘贴队列";
    [ObservableProperty] private bool isQueueView;
    [ObservableProperty] private ClipboardItemViewModel? selectedItem;
    public ObservableCollection<ClipboardItemViewModel> Items { get; } = new();
    public Visibility EmptyVisibility => IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MoreVisibility => HasMore ? Visibility.Visible : Visibility.Collapsed;
    public bool PastePlainTextOnce { get; set; }
    public string? CurrentGroup => _scope.Group;

    public LauncherViewModel(IWorkspaceService workspace, IHistoryService history, IQuickPasteService paste,
        IClipboardWriter writer, ISettingsService settings, IImageTextService ocr, IDispatcher dispatcher,
        IDebouncer debouncer, CardAssetLoader assets, ILogger<LauncherViewModel> logger)
    {
        _workspace = workspace; _history = history; _paste = paste; _writer = writer;
        _settings = settings; _ocr = ocr; _dispatcher = dispatcher; _debouncer = debouncer;
        _assets = assets; _logger = logger;
    }

    partial void OnIsEmptyChanged(bool value) => OnPropertyChanged(nameof(EmptyVisibility));
    partial void OnHasMoreChanged(bool value) => OnPropertyChanged(nameof(MoreVisibility));
    partial void OnSearchTextChanged(string value)
    {
        _queryCts?.Cancel();
        HasMore = false;
        var version = ++_searchVersion;
        _debouncer.Debounce(() => _dispatcher.Post(() => { if (version == _searchVersion) _ = RefreshAsync(); }), 200);
    }

    public void SetPasteTarget(nint handle) => _target = handle;
    public Task RefreshAsync() { _searchVersion++; return LoadAsync(false); }
    public Task LoadMoreAsync() => HasMore && !IsSearching ? LoadAsync(true) : Task.CompletedTask;
    public Task SearchNowAsync(string? query = null)
    {
        if (query is not null && SearchText != query) SearchText = query;
        return RefreshAsync();
    }

    private async Task LoadAsync(bool append)
    {
        _queryCts?.Cancel(); _queryCts?.Dispose();
        _queryCts = new CancellationTokenSource();
        var ct = _queryCts.Token;
        IsSearching = true;
        var query = _scope with { Text = SearchText };
        var offset = append ? _nextOffset : 0;
        try
        {
            IReadOnlyList<ClipboardItem> rows = IsQueueView
                ? _queue.Where(i => string.IsNullOrWhiteSpace(SearchText) || (i.GetPreviewText() ?? "").Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList()
                : await _workspace.QueryAsync(query, offset, PageSize, ct);
            ct.ThrowIfCancellationRequested();
            _nextOffset = offset + rows.Count;
            var previous = SelectedItem?.Item.Id;
            if (!append) Items.Clear();
            foreach (var row in rows)
                if (!Items.Any(i => i.Item.Id == row.Id)) Items.Add(new ClipboardItemViewModel(row, _assets));
            HasMore = !IsQueueView && rows.Count == PageSize;
            IsEmpty = Items.Count == 0;
            SelectedItem = Items.FirstOrDefault(i => i.Item.Id == previous) ?? Items.FirstOrDefault();
            StatusText = $"{ActiveList} · {Items.Count} 项" + (HasMore ? "+" : "");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ReportError(ex); }
        finally { if (!ct.IsCancellationRequested) IsSearching = false; }
    }

    private Task ChangeScopeAsync(ClipboardQuery scope, string name)
    {
        IsQueueView = false; _scope = scope; ActiveList = name;
        return RefreshAsync();
    }
    public Task ShowHistoryAsync() => ChangeScopeAsync(new(), "全部历史");
    public Task ShowFavoritesAsync() => ChangeScopeAsync(new() { FavoritesOnly = true }, "收藏");
    public Task ShowTypeAsync(ClipboardType type) => ChangeScopeAsync(new() { Type = type }, type switch
    { ClipboardType.Image => "图片", ClipboardType.Url => "链接", ClipboardType.File => "文件", _ => "代码" });
    public Task ShowGroupAsync(string group) => ChangeScopeAsync(new() { Group = group }, group);
    public Task FilterAsync(string source, DateTime? since, ClipboardType? type)
    {
        IsQueueView = false; _scope = _scope with { Source = source, Since = since, Type = type };
        return RefreshAsync();
    }
    public ClipboardQuery GetFilters() => _scope;
    public Task<IReadOnlyList<string>> GetGroupNamesAsync() => _history.GetGroupNamesAsync(CancellationToken.None);
    public Task<IReadOnlyList<Pinboard>> GetPinboardsAsync() => _workspace.GetPinboardsAsync(CancellationToken.None);
    public Task SavePinboardAsync(Pinboard board, string? oldName) => _workspace.SavePinboardAsync(board, oldName, CancellationToken.None);
    public async Task DeletePinboardAsync(string name)
    {
        await _workspace.DeletePinboardAsync(name, CancellationToken.None);
        if (CurrentGroup == name) await ShowHistoryAsync();
    }
    public Task ReorderPinboardsAsync(IReadOnlyList<string> names) => _workspace.ReorderPinboardsAsync(names, CancellationToken.None);
    public async Task SaveTextAsync(ClipboardItemViewModel? original, string title, string text)
    {
        await _workspace.SaveTextAsync(original?.Item, title, text, CancellationToken.None);
        await RefreshAsync();
    }
    public async Task<string> ExtractTextAsync(ClipboardItemViewModel item, CancellationToken ct, string? languageTag = null, bool enhanceSmallText = false)
    {
        var text = await _ocr.ExtractAsync(item.Item.ImagePath ?? throw new InvalidOperationException("图片不存在。"), ct, languageTag, enhanceSmallText);
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text)) { StatusText = "未识别到文字"; return text; }
        item.Item.OcrText = text;
        await _workspace.UpdateContentAsync(item.Item, string.Join('\n', item.Item.Title, text), ct);
        StatusText = string.IsNullOrWhiteSpace(text) ? "未识别到文字" : "图片文字已识别，可通过搜索查找";
        return text;
    }
    public Task SetGroupAsync(ClipboardItemViewModel item, string? group) => item.SetGroupAsync(_history, group);
    public async Task ToggleFavoriteAsync(ClipboardItemViewModel item)
    {
        await item.ToggleFavoriteAsync(_history);
        if (_scope.FavoritesOnly && !item.IsFavorite) await RefreshAsync();
    }
    public async Task DeleteAsync(ClipboardItemViewModel item)
    {
        await _history.DeleteAsync(item.Item.Id, CancellationToken.None);
        _queue.RemoveAll(i => i.Id == item.Item.Id); UpdateQueueLabel();
        await RefreshAsync();
    }
    public async Task BatchAsync(IReadOnlyList<ClipboardItemViewModel> selection, string action, string? group = null)
    {
        await _workspace.ApplyBatchAsync(selection.Select(i => i.Item.Id).ToArray(), action, group, CancellationToken.None);
        if (action == "delete")
        {
            var ids = selection.Select(i => i.Item.Id).ToHashSet();
            _queue.RemoveAll(i => ids.Contains(i.Id)); UpdateQueueLabel();
        }
        await RefreshAsync();
    }
    public async Task<bool> PasteCurrentAsync(bool plainText = false)
    {
        if (SelectedItem is null || _pasteBusy) return false;
        _pasteBusy = true;
        try
        {
            var item = SelectedItem.Item;
            var plain = plainText || PastePlainTextOnce || _settings.Current.AlwaysPastePlainText;
            PastePlainTextOnce = false;
            if (plain && item.Text is not null) item = new ClipboardItem { Id = item.Id, Type = ClipboardType.Text, Text = item.Text };
            var success = await _paste.PasteAsync(item, _target, CancellationToken.None);
            if (!success) StatusText = "粘贴失败，可用 Ctrl+C 复制后手动粘贴";
            else if (IsQueueView)
            {
                _queue.RemoveAll(i => i.Id == item.Id); UpdateQueueLabel();
                await RefreshAsync();
            }
            return success;
        }
        finally { _pasteBusy = false; }
    }
    public async Task PasteSelectedAsync() => await PasteCurrentAsync();
    public async Task CopySelectedAsync()
    {
        if (SelectedItem is null) return;
        try
        {
            await _writer.WriteAsync(SelectedItem.Item, Guid.NewGuid().ToString("N"), CancellationToken.None);
            StatusText = "已复制到剪贴板";
            CopyFeedback?.Invoke("已复制，可到其他应用粘贴", true);
        }
        catch (Exception ex)
        {
            LogOperationFailed(_logger, ex);
            StatusText = "复制失败，请重试";
            CopyFeedback?.Invoke("复制失败，请重试；剪贴板可能正被其他应用占用", false);
        }
    }
    public ClipboardItemViewModel? MoveSelection(int offset)
    {
        if (Items.Count == 0) return null;
        SelectedItem = Items[Math.Clamp((SelectedItem is null ? -1 : Items.IndexOf(SelectedItem)) + offset, 0, Items.Count - 1)];
        return SelectedItem;
    }
    public void AddToQueue(IEnumerable<ClipboardItemViewModel> items)
    {
        foreach (var item in items)
            if (!_queue.Any(i => i.Id == item.Item.Id)) _queue.Add(item.Item);
        UpdateQueueLabel(); StatusText = "已加入队列 · 打开队列后按 Enter 逐项粘贴";
    }
    public Task ShowQueueAsync() { IsQueueView = true; ActiveList = "粘贴队列"; return RefreshAsync(); }
    public async Task ReverseQueueAsync() { _queue.Reverse(); if (IsQueueView) await RefreshAsync(); }
    public async Task ClearQueueAsync() { _queue.Clear(); UpdateQueueLabel(); if (IsQueueView) await RefreshAsync(); }
    private void UpdateQueueLabel() => QueueLabel = _queue.Count == 0 ? "粘贴队列" : $"队列 · {_queue.Count}";
    public void Pause(int minutes)
    {
        _settings.Update(s => { s.PauseMonitoring = minutes != 0; s.PauseUntil = minutes > 0 ? DateTime.Now.AddMinutes(minutes) : null; });
        StatusText = minutes == 0 ? "已恢复记录" : $"已暂停记录 {minutes} 分钟";
    }
    public void ReportError(Exception ex)
    {
        LogOperationFailed(_logger, ex);
        StatusText = "操作失败，请重试；收藏板名称不能重复。";
    }
    [LoggerMessage(Level = LogLevel.Error, Message = "Clipboard workspace operation failed")]
    private static partial void LogOperationFailed(ILogger logger, Exception exception);
    public void Dispose() { _queryCts?.Cancel(); _queryCts?.Dispose(); _debouncer.Dispose(); GC.SuppressFinalize(this); }
}
