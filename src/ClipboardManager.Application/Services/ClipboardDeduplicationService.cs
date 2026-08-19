using ClipboardManager.Contracts;

namespace ClipboardManager.Application;

/// <summary>
/// 去重服务（规范 §15）：同一内容连续复制时，仅更新时间戳使其移动到最前。
/// </summary>
public sealed class ClipboardDeduplicationService
{
    private const int DedupeWindowMilliseconds = 2000;

    private readonly object _gate = new();
    private string? _lastHash;
    private DateTime _lastSeenUtc = DateTime.MinValue;

    /// <summary>
    /// 判定是否应跳过本次保存。
    /// 高频复制窗口内（2 秒）相同 hash 视为重复；超过窗口则允许再次入库（以支持“再次复制回到顶部”）。
    /// </summary>
    public bool IsDuplicateWithinWindow(string hash, DateTime nowUtc)
    {
        lock (_gate)
        {
            if (string.Equals(_lastHash, hash, StringComparison.Ordinal)
                && (nowUtc - _lastSeenUtc).TotalMilliseconds < DedupeWindowMilliseconds)
            {
                _lastSeenUtc = nowUtc;
                return true;
            }

            _lastHash = hash;
            _lastSeenUtc = nowUtc;
            return false;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _lastHash = null;
            _lastSeenUtc = DateTime.MinValue;
        }
    }
}
