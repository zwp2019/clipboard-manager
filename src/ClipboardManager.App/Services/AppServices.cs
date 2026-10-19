using ClipboardManager.Application;
using ClipboardManager.App.ViewModels;
using ClipboardManager.Contracts;
using ClipboardManager.Data;
using ClipboardManager.Domain;
using ClipboardManager.Infrastructure;
using ClipboardManager.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.App;

/// <summary>
/// DI 组装：Clean Architecture 依赖注入根（规范 §8、§23）。
/// </summary>
public static class AppServices
{
    public static void Register(IServiceCollection services, AppPaths paths)
    {
        // ---- Data（SQLite + Dapper + FTS5）----
        services.AddSingleton(new SqliteConnectionFactory(paths.Database));
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<ClipboardItemRepository>();
        services.AddSingleton<IClipboardItemRepository>(sp => sp.GetRequiredService<ClipboardItemRepository>());
        services.AddSingleton<IWorkspaceRepository>(sp => sp.GetRequiredService<ClipboardItemRepository>());
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<IImageTextService, ImageTextService>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<IBackupService, DatabaseBackupService>();

        // ---- Infrastructure（Win32 / WinRT）----
        services.AddSingleton<CoreHostWindow>();
        services.AddSingleton<IInternalClipboardGuard, InternalClipboardGuard>();
        services.AddSingleton<IClipboardMonitor, ClipboardMonitor>();
        services.AddSingleton<IClipboardReader, ClipboardReader>();
        services.AddSingleton<IClipboardNormalizer, ClipboardNormalizer>();
        services.AddSingleton<IClipboardHashService, ClipboardHashService>();
        services.AddSingleton<IClipboardWriter, ClipboardWriter>();
        services.AddSingleton<IImageStorageService>(
            sp => new ImageStorageService(paths.Data, sp.GetRequiredService<ILogger<ImageStorageService>>()));
        services.AddSingleton<IHotkeyService, GlobalHotkeyService>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<ITrayService>(sp => sp.GetRequiredService<TrayIconService>());
        services.AddSingleton<IWindowActivator, WindowActivator>();
        services.AddSingleton<IInputSimulator, InputSimulator>();
        services.AddSingleton<IAutoStartService, AutoStartService>();
        services.AddSingleton<INotificationService, OverlayToastService>();

        // ---- Application（应用服务编排）----
        services.AddSingleton<ClipboardDeduplicationService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IClipboardStorageService, ClipboardStorageService>();
        services.AddSingleton<ClipboardPipelineService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<IHistoryService, HistoryService>();
        services.AddSingleton<IQuickPasteService, QuickPasteService>();
        services.AddSingleton<IHistoryCleanupService, HistoryCleanupService>();

        // ---- App（UI 层）----
        services.AddSingleton<IDebouncer, TimerDebouncer>();
        services.AddSingleton<LruImageCache>();
        services.AddSingleton<CardAssetLoader>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<LauncherViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<SettingsWindow>();
    }
}
