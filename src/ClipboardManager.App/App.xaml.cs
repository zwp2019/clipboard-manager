using ClipboardManager.Application;
using ClipboardManager.Contracts;
using ClipboardManager.Data;
using ClipboardManager.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Serilog;

namespace ClipboardManager.App;

/// <summary>
/// 应用入口：DI 组装、单实例、启动服务、托盘与热键接线（规范 §33、§31、§28）。
/// 启动阶段只做：初始化、注册热键、启动监听、创建窗口（规范 §33）。
/// </summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    private const string SingleInstanceMutexName = "Local\\ClipboardManager.SingleInstance";

    private ServiceProvider? _services;
    private Mutex? _singleInstanceMutex;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private IQuickPasteService? _quickPaste;
    private ILogger<App>? _logger;
    private DispatcherQueue? _dispatcherQueue;

    private nint _previousForegroundWindow;

    public static FrameworkElement? RootElement { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!AcquireSingleInstance())
        {
            Exit();
            return;
        }

        var paths = new AppPaths();

        // 日志（不记录剪贴板内容，规范 §42）
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(paths.Logs, "clipboard-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024)
            .CreateLogger();

        _services = BuildServices(paths);
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _logger = _services.GetRequiredService<ILogger<App>>();

        try
        {
            // 数据库初始化 + 迁移（规范 §16、§33）
            _services.GetRequiredService<DatabaseInitializer>().Initialize();

            // 设置
            var settingsService = _services.GetRequiredService<ISettingsService>();
            settingsService.Load();

            // 主窗口
            _mainWindow = _services.GetRequiredService<MainWindow>();
            _mainWindow.HideRequested += (_, _) => HideLauncher();
            _mainWindow.PasteRequested += (_, _) => _ = PasteAndHideAsync();
            _mainWindow.SettingsRequested += (_, _) => ShowSettingsWindow();
            _settingsWindow = _services.GetRequiredService<SettingsWindow>();

            // 主题（规范 §47）
            var themeService = _services.GetRequiredService<ThemeService>();
            themeService.Track(_mainWindow);
            themeService.Track(_settingsWindow);
            themeService.Apply(settingsService.Current.Theme);

            // 托盘（规范 §31）
            var tray = _services.GetRequiredService<TrayIconService>();
            tray.SetPaused(settingsService.Current.PauseMonitoring);
            tray.CommandInvoked += OnTrayCommand;
            tray.Start();

            // 全局热键（规范 §28）
            var hotkeyService = _services.GetRequiredService<IHotkeyService>();
            hotkeyService.HotkeyPressed += OnHotkeyPressed;
            hotkeyService.Register(settingsService.Current.Hotkey);

            // 剪贴板管道（规范 §11）
            var pipeline = _services.GetRequiredService<ClipboardPipelineService>();
            pipeline.Start();

            // 历史过期后台清理（规范 §40）
            _services.GetRequiredService<IHistoryCleanupService>().StartAsync(CancellationToken.None);

            // 开机自启状态同步（规范 §32）
            var autoStart = _services.GetRequiredService<IAutoStartService>();
            settingsService.Update(s => s.AutoStart = autoStart.IsEnabled());

            _quickPaste = _services.GetRequiredService<IQuickPasteService>();

            // 启动即以底部横幅形态显示（Paste 风格 Dock 栏）。
            _mainWindow.PositionNearCursor();
            _mainWindow.Activate();
            _ = _mainWindow.ViewModel.RefreshAsync();
            RootElement = _mainWindow.Content as FrameworkElement;

            _logger.LogInformation("Clipboard Manager started");
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Fatal startup error");
            throw;
        }
    }

    private ServiceProvider BuildServices(AppPaths paths)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(dispose: true));
        AppServices.Register(services, paths);
        services.AddSingleton<IDispatcher>(
            _ => new WinUIDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
        return services.BuildServiceProvider();
    }

    private bool AcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        return createdNew;
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        _dispatcherQueue?.TryEnqueue(ShowLauncher);
    }

    private void OnTrayCommand(object? sender, TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.OpenClipboard:
                _dispatcherQueue?.TryEnqueue(ShowLauncher);
                break;

            case TrayCommand.Settings:
                _dispatcherQueue?.TryEnqueue(ShowSettingsWindow);
                break;

            case TrayCommand.PauseMonitoring:
                _dispatcherQueue?.TryEnqueue(() => ToggleMonitoring(true));
                break;

            case TrayCommand.ResumeMonitoring:
                _dispatcherQueue?.TryEnqueue(() => ToggleMonitoring(false));
                break;

            case TrayCommand.ClearHistory:
                _dispatcherQueue?.TryEnqueue(() => _ = ConfirmAndClearHistoryAsync());
                break;

            case TrayCommand.Quit:
                _dispatcherQueue?.TryEnqueue(ExitApplication);
                break;
        }
    }

    private void ToggleMonitoring(bool paused)
    {
        var settings = _services!.GetRequiredService<ISettingsService>();
        settings.Update(s => s.PauseMonitoring = paused);
        _services.GetRequiredService<TrayIconService>().SetPaused(paused);
        _logger?.LogInformation(paused ? "Monitoring paused" : "Monitoring resumed");
    }

    private async Task ConfirmAndClearHistoryAsync()
    {
        if (_mainWindow is null)
        {
            return;
        }

        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "清空历史",
            Content = "确定要清空全部剪贴板历史吗？此操作不可撤销。",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close,
            XamlRoot = _mainWindow.Content.XamlRoot,
        };

        if (await dialog.ShowAsync() == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
        {
            await _services!.GetRequiredService<IHistoryService>().ClearAsync(CancellationToken.None);
            _logger?.LogInformation("Clipboard history cleared");
        }
    }

    private void ShowLauncher()
    {
        if (_mainWindow is null)
        {
            return;
        }

        // 快捷键切换：已显示时再次按下则隐藏（规范 §29）。
        if (_mainWindow.IsVisible)
        {
            HideLauncher();
            return;
        }

        _previousForegroundWindow = ProcessHelper.GetForegroundWindow();
        _mainWindow.ViewModel.SetPasteTarget(_previousForegroundWindow);
        _mainWindow.PositionNearCursor();
        _mainWindow.ShowWindow();
        _ = _mainWindow.ViewModel.RefreshAsync();
    }

    private void HideLauncher()
    {
        _mainWindow?.HideWindow();
    }

    private async Task PasteAndHideAsync()
    {
        await _mainWindow!.ViewModel.PasteSelectedAsync();
        HideLauncher();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            return;
        }

        HideLauncher();
        _settingsWindow.ViewModel.Load();
        _settingsWindow.Activate();
    }

    private void ExitApplication()
    {
        try
        {
            _services?.GetRequiredService<ClipboardPipelineService>().Stop();
            _services?.GetRequiredService<ITrayService>().Stop();
            Log.Logger.Information("Clipboard Manager exited");
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Error during shutdown");
        }
        finally
        {
            Log.CloseAndFlush();
            Exit();
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // 单条异常不退出，记录后继续（规范 §43）。
        Log.Logger.Error(e.Exception, "Unhandled exception");
        e.Handled = true;
    }
}
