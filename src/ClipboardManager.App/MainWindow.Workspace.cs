using ClipboardManager.App.Controls;
using ClipboardManager.App.Services;
using ClipboardManager.App.ViewModels;
using ClipboardManager.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace ClipboardManager.App;

public sealed partial class MainWindow
{
    private bool _dialogOpen;
    private DispatcherTimer? _copyNoticeTimer;
    private void ShowCopyFeedback(string message, bool success)
    {
        _copyNoticeTimer ??= CreateCopyNoticeTimer();
        _copyNoticeTimer.Stop();
        CopyNotice.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        CopyNotice.Message = message;
        CopyNotice.IsOpen = true;
        _copyNoticeTimer.Interval = TimeSpan.FromSeconds(success ? 3 : 8);
        _copyNoticeTimer.Start();
    }
    private DispatcherTimer CreateCopyNoticeTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) => { timer.Stop(); CopyNotice.IsOpen = false; };
        return timer;
    }
    internal bool HasOpenDialog => _dialogOpen;
    private string _highlighted = "剪贴板";
    private bool _searchOpen;
    private int _openFlyouts;
    private bool IsInteractionOpen => _dialogOpen || _openFlyouts > 0;
    private IReadOnlyList<Pinboard> _boards = Array.Empty<Pinboard>();
    private static readonly string[] DateLabels = ["不限时间", "今天", "最近 7 天", "最近 30 天"];
    private static readonly string[] TypeLabels = ["全部类型", "文本", "链接", "图片", "代码", "文件", "HTML", "富文本"];

    private void UpdateBackdropTheme()
    {
        var light = RootGrid.ActualTheme == ElementTheme.Light;
        // Let the native Thin material choose ALL theme parameters together,
        // including FallbackColor. A custom tint disables automatic theme values.
        if (_acrylicController is null) RootGrid.Background = ThemeBrush("BackgroundBrush");
        if (_backdropConfiguration is not null)
            _backdropConfiguration.Theme = light ? Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Light : Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Dark;
        if (_acrylicController is not null)
            Serilog.Log.Information("Launcher native Thin acrylic theme: {Theme}; tint opacity: {Tint}; luminosity: {Luminosity}; fallback: {Fallback}",
                light ? "Light" : "Dark", _acrylicController.TintOpacity, _acrylicController.LuminosityOpacity, _acrylicController.FallbackColor);
    }
    private Brush ThemeBrush(string key)
    {
        return GlassTheme.Brush(RootGrid, key);
    }

    private static bool KeyDown(VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    private ClipboardItemViewModel[] Selection() => HistoryList.SelectedItems.OfType<ClipboardItemViewModel>().ToArray();
    private async Task SafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { ViewModel.ReportError(ex); }
    }
    private void TrackFlyout(FlyoutBase flyout)
    {
        if (flyout is MenuFlyout menu) menu.MenuFlyoutPresenterStyle = (Style)Microsoft.UI.Xaml.Application.Current.Resources["GlassMenuPresenterStyle"];
        flyout.Opening += (_, _) => { _openFlyouts++; _outsideClickDetector.Stop(); };
        flyout.Closed += (_, _) =>
        {
            _openFlyouts = Math.Max(0, _openFlyouts - 1);
            if (!IsInteractionOpen && IsVisible && !App.IsPreview) _outsideClickDetector.Start(_hwnd);
        };
    }
    private void ShowSearch()
    {
        _searchOpen = true;
        SearchHost.Width = Math.Clamp(RootGrid.ActualWidth / 4, 180, 260);
        SearchToggle.Visibility = Visibility.Collapsed;
        SearchBox.Visibility = Visibility.Visible;
        SearchBox.Focus(FocusState.Programmatic);
    }
    private void CloseSearch()
    {
        ViewModel.SearchText = string.Empty;
        _searchOpen = false;
        SearchBox.Visibility = Visibility.Collapsed;
        SearchToggle.Visibility = Visibility.Visible;
        SearchHost.Width = 32;
        HistoryList.Focus(FocusState.Programmatic);
    }
    private void SearchToggle_Click(object sender, RoutedEventArgs e) => ShowSearch();
    private void RootGrid_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (IsInteractionOpen || e.Handled || KeyDown(VirtualKey.Control) || KeyDown(VirtualKey.Menu)
            || FocusManager.GetFocusedElement(RootGrid.XamlRoot) is TextBox) return;
        if (e.Character < 33 || !System.Text.Rune.IsValid((int)e.Character)) return;
        ShowSearch();
        SearchBox.Text = char.ConvertFromUtf32((int)e.Character);
        SearchBox.SelectionStart = SearchBox.Text.Length;
        e.Handled = true;
    }
    internal async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (_dialogOpen) return ContentDialogResult.None;
        _dialogOpen = true;
        _outsideClickDetector.Stop();
        ModalShade.Visibility = Visibility.Visible;
        try { return await GlassDialogWindow.ShowAsync(this, dialog); }
        finally
        {
            _dialogOpen = false;
            ModalShade.Visibility = Visibility.Collapsed;
            if (!IsInteractionOpen && IsVisible && !App.IsPreview) _outsideClickDetector.Start(_hwnd);
        }
    }
    private void OnGlobalKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsInteractionOpen || e.Handled) return;
        if (KeyDown(VirtualKey.Control) && e.Key >= VirtualKey.Number1 && e.Key <= VirtualKey.Number9)
        {
            var index = (int)e.Key - (int)VirtualKey.Number1;
            if (index < ViewModel.Items.Count) { ViewModel.SelectedItem = ViewModel.Items[index]; PasteRequested?.Invoke(this, EventArgs.Empty); }
            e.Handled = true; return;
        }
        if (KeyDown(VirtualKey.Control) && e.Key == VirtualKey.F)
        { ShowSearch(); SearchBox.SelectAll(); e.Handled = true; return; }
        if (KeyDown(VirtualKey.Control) && e.Key == VirtualKey.N)
        { _ = SafeAsync(() => ShowDetailsAsync(null, true)); e.Handled = true; return; }
        if (KeyDown(VirtualKey.Control) && e.Key == VirtualKey.E)
        { _ = SafeAsync(() => ShowDetailsAsync(ViewModel.SelectedItem, true)); e.Handled = true; return; }
        if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Down or VirtualKey.Up)
        {
            if (FocusManager.GetFocusedElement(RootGrid.XamlRoot) is TextBox { Text.Length: > 0 } && e.Key is VirtualKey.Left or VirtualKey.Right) return;
            var item = ViewModel.MoveSelection(e.Key is VirtualKey.Left or VirtualKey.Up ? -1 : 1);
            if (item is not null) HistoryList.ScrollIntoView(item);
            if (e.Key is VirtualKey.Down or VirtualKey.Up) HistoryList.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
    }
    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_dialogOpen) return;
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            if (SearchBox.Text.Length > 0 && !KeyDown(VirtualKey.Shift))
                _ = SafeAsync(async () => { await ViewModel.SearchNowAsync(); HistoryList.Focus(FocusState.Programmatic); });
            else { ViewModel.PastePlainTextOnce = KeyDown(VirtualKey.Shift); PasteRequested?.Invoke(this, EventArgs.Empty); }
        }
        else if (e.Key == VirtualKey.Escape) { CloseSearch(); e.Handled = true; }
    }
    private void HistoryList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_dialogOpen) return;
        switch (e.Key)
        {
            case VirtualKey.Enter:
                ViewModel.PastePlainTextOnce = KeyDown(VirtualKey.Shift); PasteRequested?.Invoke(this, EventArgs.Empty); break;
            case VirtualKey.Escape:
                if (_searchOpen) CloseSearch(); else HideRequested?.Invoke(this, EventArgs.Empty); break;
            case VirtualKey.Space: _ = SafeAsync(() => ShowDetailsAsync(ViewModel.SelectedItem, false)); break;
            case VirtualKey.Delete: _ = SafeAsync(DeleteSelectionAsync); break;
            case VirtualKey.D when KeyDown(VirtualKey.Control): _ = SafeAsync(() => ViewModel.BatchAsync(Selection(), "favorite")); break;
            case VirtualKey.G when KeyDown(VirtualKey.Control): _ = SafeAsync(ShowGroupDialogAsync); break;
            case VirtualKey.A when KeyDown(VirtualKey.Control): HistoryList.SelectAll(); break;
            case VirtualKey.C when KeyDown(VirtualKey.Control): _ = SafeAsync(ViewModel.CopySelectedAsync); break;
            default: return;
        }
        e.Handled = true;
    }
    private void HistoryList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!_dialogOpen && ViewModel.SelectedItem is not null) { PasteRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }
    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItems.Count > 1) ViewModel.StatusText = $"已选择 {HistoryList.SelectedItems.Count} 项 · 可批量收藏、分组或加入队列";
    }
    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        HighlightTab(button.Tag as string ?? (button == TabFavorites ? "收藏" : "剪贴板"));
        _ = SafeAsync(async () =>
        {
            if (button.Tag is string name) await ViewModel.ShowGroupAsync(name);
            else if (button == TabFavorites) await ViewModel.ShowFavoritesAsync();
            else await ViewModel.ShowHistoryAsync();
            FilterButton.Text = "筛选当前列表…";
        });
    }
    private void HighlightTab(string name)
    {
        _highlighted = name;
        foreach (var tab in CategoryPanel.Children.OfType<Button>())
        {
            var label = tab.Tag as string ?? (tab == TabClipboard ? "剪贴板" : tab == TabFavorites ? "收藏" : "");
            var selected = !string.IsNullOrEmpty(name) && label == name;
            tab.Background = selected ? ThemeBrush("AccentSoftBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            tab.BorderThickness = new Thickness(0);
            tab.Foreground = ThemeBrush("TextPrimaryBrush");
        }
    }
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private void AddGroupButton_Click(object sender, RoutedEventArgs e) => _ = SafeAsync(() => EditBoardAsync(null));
    public async Task RefreshGroupTabsAsync()
    {
        _boards = await ViewModel.GetPinboardsAsync();
        foreach (var tab in CategoryPanel.Children.OfType<Button>().Where(b => b.Tag is string).ToList()) CategoryPanel.Children.Remove(tab);
        var index = CategoryPanel.Children.IndexOf(TabAddGroup);
        foreach (var board in _boards)
        {
            var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = ParseBrush(board.Color), VerticalAlignment = VerticalAlignment.Center };
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            label.Children.Add(dot); label.Children.Add(new TextBlock { Text = board.Name, FontSize = 12 });
            var tab = new Button { Content = label, Tag = board.Name, Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["PinboardTabStyle"], AllowDrop = true };
            tab.Click += Tab_Click;
            tab.DragOver += (_, e) => { if (e.DataView.Contains("ClipboardManager.Items")) e.AcceptedOperation = DataPackageOperation.Copy; };
            tab.Drop += (_, e) => _ = SafeAsync(async () =>
            {
                if (e.DataView.Contains("ClipboardManager.Items"))
                {
                    var raw = await e.DataView.GetDataAsync("ClipboardManager.Items") as string;
                    var ids = (raw ?? "").Split(',').Select(s => long.TryParse(s, out var id) ? id : 0).ToHashSet();
                    await ViewModel.BatchAsync(ViewModel.Items.Where(i => ids.Contains(i.Item.Id)).ToArray(), "group", board.Name);
                }
            });
            var menu = new MenuFlyout();
            AddMenu(menu, "编辑名称与颜色", () => EditBoardAsync(board));
            AddMenu(menu, "向前移动", () => MoveBoardAsync(board, -1));
            AddMenu(menu, "向后移动", () => MoveBoardAsync(board, 1));
            AddMenu(menu, "删除收藏板", async () =>
            {
                if (await ShowDialogAsync(new ContentDialog { Title = "删除收藏板？", Content = "条目会保留在历史中。", PrimaryButtonText = "删除", CloseButtonText = "取消" }) == ContentDialogResult.Primary)
                { await ViewModel.DeletePinboardAsync(board.Name); await RefreshGroupTabsAsync(); HighlightTab("剪贴板"); }
            });
            tab.ContextFlyout = menu;
            TrackFlyout(menu);
            CategoryPanel.Children.Insert(index++, tab);
        }
        HighlightTab(_highlighted);
    }
    private static SolidColorBrush ParseBrush(string hex)
    {
        var value = Convert.ToInt32(hex.TrimStart('#'), 16);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value));
    }
    private async Task EditBoardAsync(Pinboard? board)
    {
        var name = new TextBox { Header = "名称", Text = board?.Name ?? "", MaxLength = 60, MinWidth = 300 };
        var colors = new[] { "#7C6AF2", "#E76A8A", "#49A7A0", "#E2A34F", "#5B98DC", "#8B96A5" };
        var color = new ComboBox { Header = "颜色", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var hex in colors)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = ParseBrush(hex) });
            row.Children.Add(new TextBlock { Text = hex });
            color.Items.Add(new ComboBoxItem { Content = row, Tag = hex });
        }
        color.SelectedIndex = Math.Max(0, Array.IndexOf(colors, board?.Color));
        var panel = new StackPanel { Spacing = 16 }; panel.Children.Add(name); panel.Children.Add(color);
        if (await ShowDialogAsync(new ContentDialog { Title = board is null ? "新建收藏板" : "编辑收藏板", Content = panel, PrimaryButtonText = "保存", CloseButtonText = "取消" }) != ContentDialogResult.Primary) return;
        await ViewModel.SavePinboardAsync(new Pinboard { Name = name.Text, Color = (string)((ComboBoxItem)color.SelectedItem).Tag }, board?.Name);
        await RefreshGroupTabsAsync(); HighlightTab(name.Text.Trim()); await ViewModel.ShowGroupAsync(name.Text.Trim());
    }
    private async Task MoveBoardAsync(Pinboard board, int offset)
    {
        var names = _boards.Select(b => b.Name).ToList(); var index = names.IndexOf(board.Name);
        var next = Math.Clamp(index + offset, 0, names.Count - 1);
        (names[index], names[next]) = (names[next], names[index]);
        await ViewModel.ReorderPinboardsAsync(names); await RefreshGroupTabsAsync();
    }
    private async Task ShowGroupDialogAsync()
    {
        var selection = Selection(); if (selection.Length == 0) return;
        var groups = await ViewModel.GetGroupNamesAsync();
        var combo = new ComboBox { Header = "收藏板", MinWidth = 300, HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.Items.Add("移出收藏板"); foreach (var group in groups) combo.Items.Add(group); combo.SelectedIndex = 0;
        if (await ShowDialogAsync(new ContentDialog { Title = $"移动 {selection.Length} 项", Content = combo, PrimaryButtonText = "移动", CloseButtonText = "取消" }) != ContentDialogResult.Primary) return;
        await ViewModel.BatchAsync(selection, "group", combo.SelectedIndex == 0 ? null : combo.SelectedItem as string);
    }
    private async Task ShowDetailsAsync(ClipboardItemViewModel? item, bool edit)
    {
        if (item is null && !edit) return;
        var details = new ItemDetails(ViewModel, item, edit);
        var result = await ShowDialogAsync(new ContentDialog
        {
            Title = item is null ? "新建常用文本" : edit ? "编辑条目" : "内容预览",
            Content = details, PrimaryButtonText = edit ? "保存" : "编辑", CloseButtonText = "关闭",
        });
        if (result != ContentDialogResult.Primary) return;
        if (!edit) { await ShowDetailsAsync(item, true); return; }
        await ViewModel.SaveTextAsync(item, details.ItemTitle, details.ItemText);
    }
    private void NewItem_Click(object sender, RoutedEventArgs e) => _ = SafeAsync(() => ShowDetailsAsync(null, true));
    private void Filter_Click(object sender, RoutedEventArgs e) => _ = SafeAsync(async () =>
    {
        var filters = ViewModel.GetFilters();
        var source = new TextBox { Header = "来源应用", PlaceholderText = "例如 chrome、code、微信", Text = filters.Source ?? "", MinWidth = 300 };
        var date = new ComboBox { Header = "复制时间", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var label in DateLabels) date.Items.Add(label);
        date.SelectedIndex = filters.Since is null ? 0 : filters.Since == DateTime.Today ? 1 : filters.Since > DateTime.Today.AddDays(-8) ? 2 : 3;
        var type = new ComboBox { Header = "内容类型", HorizontalAlignment = HorizontalAlignment.Stretch };
        var types = new ClipboardType?[] { null, ClipboardType.Text, ClipboardType.Url, ClipboardType.Image, ClipboardType.Code, ClipboardType.File, ClipboardType.Html, ClipboardType.Rtf };
        foreach (var label in TypeLabels) type.Items.Add(label);
        type.SelectedIndex = Array.IndexOf(types, filters.Type);
        var panel = new StackPanel { Spacing = 16 }; panel.Children.Add(source); panel.Children.Add(date); panel.Children.Add(type);
        var result = await ShowDialogAsync(new ContentDialog { Title = "筛选当前列表", Content = panel, PrimaryButtonText = "应用", SecondaryButtonText = "重置", CloseButtonText = "取消" });
        if (result == ContentDialogResult.None) return;
        if (result == ContentDialogResult.Secondary) { await ViewModel.FilterAsync("", null, null); FilterButton.Text = "筛选当前列表…"; }
        else
        {
            DateTime? since = date.SelectedIndex switch { 1 => DateTime.Today, 2 => DateTime.Today.AddDays(-6), 3 => DateTime.Today.AddDays(-29), _ => null };
            await ViewModel.FilterAsync(source.Text, since, types[type.SelectedIndex]);
            FilterButton.Text = string.IsNullOrWhiteSpace(source.Text) && since is null && type.SelectedIndex == 0 ? "筛选当前列表…" : "筛选当前列表 · 已启用";
        }
    });
    private void Card_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ClipboardItemViewModel item) { ShowCardMenu(element, item); e.Handled = true; }
    }
    private void ShowCardMenu(FrameworkElement anchor, ClipboardItemViewModel item)
    {
        ViewModel.SelectedItem = item;
        var menu = new MenuFlyout();
        AddMenu(menu, "粘贴   Enter", () => { PasteRequested?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; });
        AddMenu(menu, "粘贴为纯文本   Shift+Enter", () => { ViewModel.PastePlainTextOnce = true; PasteRequested?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; });
        AddMenu(menu, "复制   Ctrl+C", ViewModel.CopySelectedAsync);
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenu(menu, "预览   Space", () => ShowDetailsAsync(item, false));
        AddMenu(menu, "编辑与重命名   Ctrl+E", () => ShowDetailsAsync(item, true));
        AddMenu(menu, item.IsFavorite ? "取消收藏" : "收藏", () => ViewModel.ToggleFavoriteAsync(item));
        AddMenu(menu, "移入收藏板", ShowGroupDialogAsync);
        AddMenu(menu, "加入粘贴队列", () => { ViewModel.AddToQueue(new[] { item }); return Task.CompletedTask; });
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenu(menu, "删除", () => ViewModel.DeleteAsync(item));
        TrackFlyout(menu);
        menu.ShowAt(anchor);
    }
    private void AddMenu(MenuFlyout menu, string text, Func<Task> action)
    {
        var parts = text.Split("   ", 2, StringSplitOptions.None);
        var entry = new MenuFlyoutItem { Text = parts[0] };
        if (parts.Length == 2) entry.KeyboardAcceleratorTextOverride = parts[1];
        var glyph = parts[0] switch
        {
            "粘贴" => "\uE77F", "复制" => "\uE8C8", "预览" => "\uE890", "编辑与重命名" => "\uE70F",
            "收藏" => "\uE735", "取消收藏" => "\uE734", "移入收藏板" => "\uE8B7", "删除" => "\uE74D", _ => null,
        };
        if (glyph is not null) entry.Icon = new FontIcon { Glyph = glyph, FontSize = 14 };
        if (parts[0].StartsWith("删除", StringComparison.Ordinal)) entry.Foreground = ThemeBrush("DangerBrush");
        entry.Click += (_, _) => _ = SafeAsync(action);
        menu.Items.Add(entry);
    }
    private void LoadMore_Click(object sender, RoutedEventArgs e) => _ = SafeAsync(ViewModel.LoadMoreAsync);
    private void HistoryList_Loaded(object sender, RoutedEventArgs e)
    {
        var scroll = FindScrollViewer(HistoryList);
        if (scroll is not null) scroll.ViewChanged += (_, args) =>
        {
            if (!args.IsIntermediate && scroll.ScrollableWidth > 0 && scroll.HorizontalOffset >= scroll.ScrollableWidth - 300)
                _ = SafeAsync(ViewModel.LoadMoreAsync);
        };
    }
    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var result = FindScrollViewer(VisualTreeHelper.GetChild(parent, i)); if (result is not null) return result; }
        return null;
    }
    private void HistoryList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var items = e.Items.OfType<ClipboardItemViewModel>().ToArray();
        e.Data.SetData("ClipboardManager.Items", string.Join(',', items.Select(i => i.Item.Id)));
        e.Data.RequestedOperation = DataPackageOperation.Copy;
        var text = string.Join(Environment.NewLine, items.Select(i => i.Item.Text).Where(t => !string.IsNullOrEmpty(t)));
        if (text.Length > 0) e.Data.SetText(text);
        if (!items.Any(i => i.Item.Type is ClipboardType.Image or ClipboardType.File)) return;
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
          var deferral = request.GetDeferral();
          try
          {
            var files = new List<IStorageItem>();
            foreach (var item in items)
            {
                if (item.Item.Type == ClipboardType.Image && File.Exists(item.Item.ImagePath)) files.Add(await StorageFile.GetFileFromPathAsync(item.Item.ImagePath));
                if (item.Item.Type == ClipboardType.File)
                    foreach (var path in System.Text.Json.JsonSerializer.Deserialize<string[]>(item.Item.FilePaths ?? "[]") ?? [])
                    {
                        if (File.Exists(path)) files.Add(await StorageFile.GetFileFromPathAsync(path));
                        else if (Directory.Exists(path)) files.Add(await StorageFolder.GetFolderFromPathAsync(path));
                    }
            }
            request.SetData(files);
          }
          catch (Exception ex) { ViewModel.ReportError(ex); }
          finally { deferral.Complete(); }
        });
    }
    private void SelectAll_Click(object sender, RoutedEventArgs e) => HistoryList.SelectAll();
    private void BatchFavorite_Click(object sender, RoutedEventArgs e) => _ = SafeAsync(() => ViewModel.BatchAsync(Selection(), "favorite"));
    private void BatchGroup_Click(object sender, RoutedEventArgs e) => _ = SafeAsync(ShowGroupDialogAsync);
    private void BatchDelete_Click(object sender, RoutedEventArgs e) => _ = SafeAsync(DeleteSelectionAsync);
    private async Task DeleteSelectionAsync()
    {
        var items = Selection(); if (items.Length == 0) return;
        if (await ShowDialogAsync(new ContentDialog { Title = $"删除 {items.Length} 项？", Content = "删除后无法撤销。", PrimaryButtonText = "删除", CloseButtonText = "取消" }) == ContentDialogResult.Primary)
            await ViewModel.BatchAsync(items, "delete");
    }
    private void QueueOpen_Click(object sender, RoutedEventArgs e) => _ = SafeAsync(async () => { HighlightTab(""); await ViewModel.ShowQueueAsync(); });
    private void QueueAdd_Click(object sender, RoutedEventArgs e) => ViewModel.AddToQueue(Selection());
    private void QueueReverse_Click(object sender, RoutedEventArgs e) => _ = SafeAsync(ViewModel.ReverseQueueAsync);
    private void QueueClear_Click(object sender, RoutedEventArgs e) => _ = SafeAsync(ViewModel.ClearQueueAsync);
    private void Pause_Click(object sender, RoutedEventArgs e) => ViewModel.Pause(int.Parse((string)((MenuFlyoutItem)sender).Tag, System.Globalization.CultureInfo.InvariantCulture));

    private void TypeMenu_Click(object sender, RoutedEventArgs e) => _ = SafeAsync(async () =>
    {
        if (sender is not MenuFlyoutItem { Tag: string tag }) return;
        if (tag == "All") await ViewModel.FilterAsync("", null, null);
        else if (Enum.TryParse<ClipboardType>(tag, out var type))
        {
            var current = ViewModel.GetFilters();
            await ViewModel.FilterAsync(current.Source ?? "", current.Since, type);
        }
        FilterButton.Text = tag == "All" ? "筛选当前列表…" : "筛选当前列表 · 已启用";
    });
    private void ShortcutHelp_Click(object sender, RoutedEventArgs e) => _ = SafeAsync(async () =>
    {
        await ShowDialogAsync(new ContentDialog
        {
            Title = "快捷键",
            Content = "直接输入 / Ctrl+F　搜索\n← / →　选择内容\nEnter　粘贴\nShift+Enter　粘贴为纯文本\nCtrl+1…9　粘贴对应内容\nSpace　完整预览\nCtrl+E　编辑与重命名\nCtrl+N　新建常用文本\nCtrl+C　复制\nCtrl+D　收藏所选\nCtrl+G　移入收藏板\nCtrl+A　选择已加载的全部内容\nCtrl / Shift+单击　多选\nDelete　删除所选\nEsc　退出搜索 / 收起窗口",
            CloseButtonText = "知道了",
        });
    });
    private void HistoryList_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var scroll = FindScrollViewer(HistoryList);
        if (scroll is not null) ScrollHorizontally(scroll, e);
    }
    private void CategoryScroll_PointerWheelChanged(object sender, PointerRoutedEventArgs e) => ScrollHorizontally(CategoryScroll, e);
    private static void ScrollHorizontally(ScrollViewer scroll, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(scroll).Properties;
        if (properties.IsHorizontalMouseWheel || scroll.ScrollableWidth <= 0) return;
        var delta = properties.MouseWheelDelta / 120.0 * 180;
        scroll.ChangeView(Math.Clamp(scroll.HorizontalOffset - delta, 0, scroll.ScrollableWidth), null, null, true);
        e.Handled = true;
    }
}
