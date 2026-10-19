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
public partial class App : Microsoft.UI.Xaml.Application, IDisposable
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

    internal static bool IsPreview => Environment.GetCommandLineArgs().Contains("--preview", StringComparer.Ordinal)
        || string.Equals(Path.GetFileName(Environment.ProcessPath), "ClipboardManager.Preview.exe", StringComparison.OrdinalIgnoreCase);

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
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
                fileSizeLimitBytes: 10 * 1024 * 1024,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture)
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

            // 全局热键（规范 §28）：失败时托盘气泡告知用户（常见于热键被占用），并延迟自动重试。
            var hotkeyService = _services.GetRequiredService<IHotkeyService>();
            hotkeyService.HotkeyPressed += OnHotkeyPressed;
            if (!IsPreview && !hotkeyService.Register(settingsService.Current.Hotkey))
            {
                tray.ShowBalloon(
                    "快捷键注册失败",
                    $"快捷键 {settingsService.Current.Hotkey} 可能被其他程序占用，可在设置中更换；5 秒后将自动重试。");

                var retryTimer = _dispatcherQueue.CreateTimer();
                retryTimer.Interval = TimeSpan.FromSeconds(5);
                retryTimer.IsRepeating = false;
                retryTimer.Tick += (_, _) =>
                {
                    if (hotkeyService.Register(settingsService.Current.Hotkey))
                    {
                        tray.ShowBalloon("快捷键已恢复", $"快捷键 {settingsService.Current.Hotkey} 注册成功。");
                    }
                };
                retryTimer.Start();
            }

            // 剪贴板管道（规范 §11）
            var pipeline = _services.GetRequiredService<ClipboardPipelineService>();
            if (!IsPreview) pipeline.Start();

            // 历史过期后台清理（规范 §40）
            if (!IsPreview) _ = _services.GetRequiredService<IHistoryCleanupService>().StartAsync(CancellationToken.None);

            // 开机自启状态同步（规范 §32）
            var autoStart = _services.GetRequiredService<IAutoStartService>();
            if (!IsPreview) settingsService.Update(s => s.AutoStart = autoStart.IsEnabled());

            _quickPaste = _services.GetRequiredService<IQuickPasteService>();

            if (IsPreview) await SeedPreviewAsync();
            _previousForegroundWindow = ProcessHelper.GetForegroundWindow();
            _mainWindow.ViewModel.SetPasteTarget(_previousForegroundWindow);

            // 启动即以底部横幅形态显示（Paste 风格 Dock 栏）。
            _mainWindow.PositionNearCursor();
            _mainWindow.ShowWindow();
            _ = _mainWindow.ViewModel.RefreshAsync();
            RootElement = _mainWindow.Content as FrameworkElement;

            LogMessages.Started(_logger);
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Fatal startup error");
            throw;
        }
    }

    private static ServiceProvider BuildServices(AppPaths paths)
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
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName + (IsPreview ? ".Preview" : ""), out var createdNew);
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
        settings.Update(s => { s.PauseMonitoring = paused; s.PauseUntil = null; });
        _services!.GetRequiredService<TrayIconService>().SetPaused(paused);
        if (_logger is not null)
        {
            if (paused)
            {
                LogMessages.MonitoringPaused(_logger);
            }
            else
            {
                LogMessages.MonitoringResumed(_logger);
            }
        }
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

        if (await _mainWindow.ShowDialogAsync(dialog) == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
        {
            await _services!.GetRequiredService<IHistoryService>().ClearAsync(CancellationToken.None);
            if (_logger is not null)
            {
                LogMessages.HistoryCleared(_logger);
            }
        }
    }

    private void ShowLauncher()
    {
        if (_mainWindow?.HasOpenDialog == true) return;
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
        if (await _mainWindow!.ViewModel.PasteCurrentAsync()) HideLauncher();
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
            _mainWindow?.Dispose();
            Log.Logger.Information("Clipboard Manager exited");
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Error during shutdown");
        }
        finally
        {
            Dispose();
            Log.CloseAndFlush();
            Exit();
        }
    }

    /// <summary>
    /// 释放单实例互斥体。服务容器与其余资源由进程退出时统一回收，日志已由 ExitApplication 关闭。
    /// </summary>
    public void Dispose()
    {
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        GC.SuppressFinalize(this);
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // 单条异常不退出，记录后继续（规范 §43）。
        Log.Logger.Error(e.Exception, "Unhandled exception");
        e.Handled = true;
    }

    /// <summary>
    /// 源生成日志方法（避免与 Serilog 的静态 Log 类重名，故命名为 LogMessages）。
    /// 参数类型需完整限定：本文件同时 using Serilog，ILogger 存在歧义。
    /// </summary>
    private static partial class LogMessages
    {
        [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "Clipboard Manager started")]
        public static partial void Started(Microsoft.Extensions.Logging.ILogger logger);

        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Monitoring paused")]
        public static partial void MonitoringPaused(Microsoft.Extensions.Logging.ILogger logger);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Monitoring resumed")]
        public static partial void MonitoringResumed(Microsoft.Extensions.Logging.ILogger logger);

        [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Clipboard history cleared")]
        public static partial void HistoryCleared(Microsoft.Extensions.Logging.ILogger logger);
    }
}
