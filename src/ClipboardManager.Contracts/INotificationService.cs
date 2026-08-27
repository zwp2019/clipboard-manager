using ClipboardManager.Domain;

namespace ClipboardManager.Contracts;

/// <summary>
/// 系统 Toast 通知服务（剪贴板捕获成功后的轻提示）。
/// </summary>
public interface INotificationService
{
    /// <summary>新内容成功入库后弹出系统通知。</summary>
    void ShowItemCaptured(ClipboardItem item);
}
