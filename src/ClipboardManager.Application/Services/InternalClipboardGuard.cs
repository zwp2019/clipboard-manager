using ClipboardManager.Contracts;

namespace ClipboardManager.Application;

/// <summary>Session identity survives the write scope, so queued clipboard notifications
/// can still be recognized without suppressing unrelated copies for a time window.</summary>
public sealed class InternalClipboardGuard : IInternalClipboardGuard
{
    private readonly string _markerPrefix = $"v1:{Guid.NewGuid():N}:";
    private readonly object _gate = new();
    private readonly List<WriteScope> _scopes = new();

    public bool IsOwnWrite { get { lock (_gate) return _scopes.Count > 0; } }
    public string? CurrentOperationId { get { lock (_gate) return _scopes.LastOrDefault()?.OperationId; } }
    public string CreateClipboardMarker(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        return _markerPrefix + operationId;
    }
    public bool IsOwnClipboardMarker(string? marker) => marker is not null
        && marker.StartsWith(_markerPrefix, StringComparison.Ordinal) && marker.Length > _markerPrefix.Length;
    public IDisposable BeginOwnWrite(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var scope = new WriteScope(this, operationId);
        lock (_gate) _scopes.Add(scope);
        return scope;
    }
    private sealed class WriteScope(InternalClipboardGuard owner, string operationId) : IDisposable
    {
        public string OperationId { get; } = operationId;
        public void Dispose() { lock (owner._gate) owner._scopes.Remove(this); }
    }
}
