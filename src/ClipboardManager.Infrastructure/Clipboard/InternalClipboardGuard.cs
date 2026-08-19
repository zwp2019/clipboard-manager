using ClipboardManager.Contracts;

namespace ClipboardManager.Infrastructure;

/// <summary>
/// 内部剪贴板操作守卫：标记当前是否为程序自身写入，防止监听死循环（规范 §13）。
/// </summary>
public sealed class InternalClipboardGuard : IInternalClipboardGuard
{
    private int _ownWrite;
    private string? _operationId;

    public bool IsOwnWrite => Volatile.Read(ref _ownWrite) == 1;

    public string? CurrentOperationId => Interlocked.CompareExchange(ref _operationId!, null, null);

    public IDisposable BeginOwnWrite(string operationId)
    {
        Interlocked.Exchange(ref _ownWrite, 1);
        Interlocked.Exchange(ref _operationId, operationId);
        return new WriteScope(this);
    }

    private sealed class WriteScope : IDisposable
    {
        private readonly InternalClipboardGuard _owner;
        private int _disposed;

        public WriteScope(InternalClipboardGuard owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.EndOwnWrite();
            }
        }
    }

    private void EndOwnWrite()
    {
        Interlocked.Exchange(ref _ownWrite, 0);
        Interlocked.Exchange(ref _operationId, null);
    }
}
