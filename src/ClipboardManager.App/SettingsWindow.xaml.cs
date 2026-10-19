using System.Globalization;
using ClipboardManager.App.ViewModels;
using ClipboardManager.App.Controls;
using ClipboardManager.App.Services;
using ClipboardManager.Contracts;
using ClipboardManager.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;

namespace ClipboardManager.App;

/// <summary>
/// 设置窗口（规范 §46）。数据管理（备份/恢复/清空）涉及文件选择器，保留在 Code-behind。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The backdrop is released by the native Window.Closed event.")]
public sealed partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly IBackupService _backupService;
    private readonly IHistoryService _historyService;
    private readonly nint _hwnd;
    private readonly AppPaths _paths;
    private readonly GlassWindowBackdrop _glassBackdrop;

    public SettingsViewModel ViewModel => _viewModel;

    public SettingsWindow(
        SettingsViewModel viewModel,
        IBackupService backupService,
        IHistoryService historyService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _backupService = backupService;
        _historyService = historyService;
        _paths = new AppPaths();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _glassBackdrop = new GlassWindowBackdrop(this, SettingsRoot);
        Closed += (_, _) => _glassBackdrop.Dispose();
    }

    // ---- 热键捕获（规范 §28）----

    private void HotkeyBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var key = e.Key;

        // 忽略纯修饰键按下
        if (key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            e.Handled = true;
            return;
        }

        var keyName = KeyToString(key);
        if (keyName is null)
        {
            e.Handled = true;
            return;
        }

        var parts = new List<string>();
        if (IsKeyDown(VirtualKey.Control))
        {
            parts.Add("Ctrl");
        }

        if (IsKeyDown(VirtualKey.Shift))
        {
            parts.Add("Shift");
        }

        if (IsKeyDown(VirtualKey.Menu))
        {
            parts.Add("Alt");
        }

        if (IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows))
        {
            parts.Add("Win");
        }

        parts.Add(keyName);
        ViewModel.TryApplyHotkey(string.Join("+", parts));
        e.Handled = true;
    }

    private static string? KeyToString(VirtualKey key)
    {
        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
        {
            return ((int)(key - VirtualKey.Number0)).ToString(CultureInfo.InvariantCulture);
        }

        if (key >= VirtualKey.A && key <= VirtualKey.Z)
        {
            return key.ToString();
        }

        if (key >= VirtualKey.F1 && key <= VirtualKey.F12)
        {
            return key.ToString();
        }

        return key switch
        {
            VirtualKey.Space => "Space",
            VirtualKey.Enter => "Enter",
            VirtualKey.Tab => "Tab",
            VirtualKey.Escape => "Esc",
            VirtualKey.Back => "Backspace",
            VirtualKey.Delete => "Delete",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            VirtualKey.PageUp => "PageUp",
            VirtualKey.PageDown => "PageDown",
            VirtualKey.Insert => "Insert",
            _ => null,
        };
    }

    private static bool IsKeyDown(VirtualKey key)
    {
        var coreWindow = CoreWindow.GetForCurrentThread();
        if (coreWindow is null)
        {
            return false;
        }

        var state = coreWindow.GetKeyState(key);
        return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }

    // ---- 排除应用（规范 §39）----

    private void AddExcludedApp_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AddExcludedApplication(ExcludeAppBox.Text.Trim());
        ExcludeAppBox.Text = string.Empty;
    }

    private void AddCurrentApp_Click(object sender, RoutedEventArgs e)
    {
        var foreground = ProcessHelper.GetForegroundWindow();
        var appName = ProcessHelper.GetProcessName(foreground);
        if (!string.IsNullOrEmpty(appName))
        {
            ViewModel.AddExcludedApplication(appName);
        }
    }

    private void RemoveExcludedApp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string appName)
        {
            ViewModel.RemoveExcludedApplication(appName);
        }
    }

    // ---- 数据管理 ----

    private async void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"clipboard-backup-{DateTime.Now:yyyyMMdd-HHmmss}",
            DefaultFileExtension = ".db",
        };
        picker.FileTypeChoices.Add("SQLite Database", new List<string> { ".db" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(file.Path) ?? _paths.Backups;
            var backupPath = await _backupService.CreateBackupAsync(directory, CancellationToken.None);
            await ShowInfoAsync("备份完成", $"数据库已备份到：\n{backupPath}");
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("备份失败", ex.Message);
        }
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = await ConfirmAsync(
            "恢复数据库",
            "恢复将用备份文件替换当前数据库。\n\n建议先通过托盘退出应用再恢复；直接恢复可能因数据库被占用而失败。是否继续？");
        if (!confirmed)
        {
            return;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".db");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            await _backupService.RestoreAsync(file.Path, CancellationToken.None);
            await ShowInfoAsync("恢复完成", "数据库已恢复，建议重启应用。");
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("恢复失败", ex.Message);
        }
    }

    private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = await ConfirmAsync("清空历史", "确定要清空全部剪贴板历史吗？此操作不可撤销。");
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _historyService.ClearAsync(CancellationToken.None);
            await ShowInfoAsync("完成", "剪贴板历史已清空。");
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("操作失败", ex.Message);
        }
    }

    // ---- 保存 / 取消 ----

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Save();
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ---- 对话框辅助 ----

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        return await GlassDialogWindow.ShowAsync(this, dialog) == ContentDialogResult.Primary;
    }

    private async Task ShowInfoAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = Content.XamlRoot,
        };
        await GlassDialogWindow.ShowAsync(this, dialog);
    }
}
