using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using Microsoft.Extensions.Logging;

namespace ClipboardManager.Application;

/// <summary>
/// 快速粘贴服务（规范 §30）：写剪贴板 → 激活前一个窗口 → 发送 Ctrl+V。
/// </summary>
public sealed class QuickPasteService : IQuickPasteService
{
    private readonly IClipboardWriter _writer;
    private readonly IWindowActivator _windowActivator;
    private readonly IInputSimulator _inputSimulator;
    private readonly ILogger<QuickPasteService> _logger;

    public QuickPasteService(
        IClipboardWriter writer,
        IWindowActivator windowActivator,
        IInputSimulator inputSimulator,
        ILogger<QuickPasteService> logger)
    {
        _writer = writer;
        _windowActivator = windowActivator;
        _inputSimulator = inputSimulator;
        _logger = logger;
    }

    public async Task<bool> PasteAsync(ClipboardItem item, nint targetWindowHandle, CancellationToken ct)
    {
        if (item is null)
        {
            return false;
        }

        try
        {
            // 1. 写入剪贴板（携带内部操作标记，防止被自己监听产生死循环，规范 §13）
            var operationId = Guid.NewGuid().ToString("N");
            await _writer.WriteAsync(item, operationId, ct);

            // 2. 激活目标窗口（通常是打开 Launcher 前的窗口）
            if (targetWindowHandle != nint.Zero)
            {
                _windowActivator.ActivateWindow(targetWindowHandle);
            }

            // 3. 发送粘贴快捷键
            _inputSimulator.SendPaste();

            _logger.LogInformation("Quick paste executed for item {ItemId} ({Type})", item.Id, item.Type);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Quick paste failed for item {ItemId}", item.Id);
            return false;
        }
    }
}
