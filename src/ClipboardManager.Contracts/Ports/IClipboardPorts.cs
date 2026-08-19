using ClipboardManager.Domain;

namespace ClipboardManager.Contracts;

/// <summary>
/// 剪贴板监听器：基于 Windows 原生事件（WM_CLIPBOARDUPDATE），禁止轮询（规范 §12）。
/// </summary>
public interface IClipboardMonitor
{
    event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;

    bool IsRunning { get; }

    void Start();

    void Stop();
}

/// <summary>
/// 剪贴板读取器：负责从 Windows 剪贴板读取各格式内容，含重试与异常处理（规范 §44）。
/// </summary>
public interface IClipboardReader
{
    Task<ClipboardData?> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 归一化器：将原始 ClipboardData 转换为统一的 ClipboardItem（规范 §11）。
/// </summary>
public interface IClipboardNormalizer
{
    ClipboardItem Normalize(ClipboardData data, string hash);
}

/// <summary>
/// 哈希服务：为剪贴板内容计算 SHA-256 指纹（规范 §14）。
/// </summary>
public interface IClipboardHashService
{
    string ComputeHash(ClipboardData data);
}

/// <summary>
/// 剪贴板写入器：快速粘贴时向剪贴板写入内容，并标记为内部操作防死循环（规范 §13、§30）。
/// </summary>
public interface IClipboardWriter
{
    Task WriteAsync(ClipboardItem item, string operationId, CancellationToken cancellationToken);
}

/// <summary>
/// 内部剪贴板操作守卫：标记当前是否为程序自身写入（规范 §13）。
/// </summary>
public interface IInternalClipboardGuard
{
    bool IsOwnWrite { get; }

    string? CurrentOperationId { get; }

    IDisposable BeginOwnWrite(string operationId);
}
