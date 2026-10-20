using ClipboardManager.Domain;

namespace ClipboardManager.Contracts;

/// <summary>
/// 系统 Toast 通知服务（剪贴板捕获成功后的轻提示）。
/// </summary>
public interface INotificationService
{
    /// <summary>新内容成功入库后弹出系统通知。</summary>
    void ShowItemCaptured(ClipboardItem item);

    /// <summary>复制面板收起后显示操作结果及内容摘要，不抢占输入焦点。</summary>
    void ShowCopyResult(ClipboardItem item, bool success);
}
