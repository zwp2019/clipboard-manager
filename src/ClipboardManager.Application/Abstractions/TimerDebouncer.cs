using ClipboardManager.Contracts;

namespace ClipboardManager.Application;

/// <summary>
/// 基于 System.Threading.Timer 的防抖器：延迟窗口内只执行最后一次（规范 §27）。
/// 回调在 ThreadPool 执行，UI 更新需通过 IDispatcher 切换线程。
/// </summary>
public sealed class TimerDebouncer : IDebouncer
{
    private readonly object _gate = new();
    private Timer? _timer;

    public void Debounce(Action action, int delayMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_gate)
        {
            _timer?.Dispose();
            _timer = new Timer(
                _ =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception)
                    {
                        // 防抖回调异常不应导致进程退出；由上层各自捕获处理。
                    }
                },
                null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _timer.Change(delayMilliseconds, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
