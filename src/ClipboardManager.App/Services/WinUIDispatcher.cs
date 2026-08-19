using ClipboardManager.Contracts;
using Microsoft.UI.Dispatching;

namespace ClipboardManager.App;

/// <summary>
/// WinUI DispatcherQueue 实现 IDispatcher：所有 UI 更新回到 UI 线程（规范 §36）。
/// </summary>
public sealed class WinUIDispatcher : IDispatcher
{
    private readonly DispatcherQueue _queue;

    public WinUIDispatcher(DispatcherQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public void Post(Action action)
    {
        if (!_queue.TryEnqueue(() => action()))
        {
            throw new InvalidOperationException("Dispatcher queue is shutting down.");
        }
    }

    public Task PostAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var posted = _queue.TryEnqueue(
            () =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });

        if (!posted)
        {
            completion.SetCanceled();
        }

        return completion.Task;
    }
}
