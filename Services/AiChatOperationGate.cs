using System;
using System.Threading;

namespace SystemTools.Services;

public sealed class AiChatOperationGate
{
    private readonly object _syncRoot = new();
    private OperationLease? _activeLease;

    public event EventHandler? StateChanged;

    public bool IsBusy
    {
        get
        {
            lock (_syncRoot)
            {
                return _activeLease is not null;
            }
        }
    }

    public bool IsGenerationActive
    {
        get
        {
            lock (_syncRoot)
            {
                return _activeLease?.Kind == OperationKind.Generation;
            }
        }
    }

    public IDisposable? TryAcquireGeneration(object owner)
    {
        return TryAcquire(owner, OperationKind.Generation);
    }

    public IDisposable? TryAcquireAttachmentUpdate(object owner)
    {
        return TryAcquire(owner, OperationKind.AttachmentUpdate);
    }

    private IDisposable? TryAcquire(object owner, OperationKind kind)
    {
        ArgumentNullException.ThrowIfNull(owner);
        OperationLease lease;
        lock (_syncRoot)
        {
            if (_activeLease is not null)
            {
                return null;
            }

            lease = new OperationLease(this, kind);
            _activeLease = lease;
        }

        RaiseStateChanged();
        return lease;
    }

    private void Release(OperationLease lease)
    {
        lock (_syncRoot)
        {
            if (!ReferenceEquals(_activeLease, lease))
            {
                return;
            }

            _activeLease = null;
        }

        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        foreach (EventHandler handler in StateChanged?.GetInvocationList() ?? [])
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch
            {
                // A UI subscriber must not strand or incorrectly release an operation lease.
            }
        }
    }

    internal enum OperationKind
    {
        Generation,
        AttachmentUpdate
    }

    private sealed class OperationLease(AiChatOperationGate gate, OperationKind kind) : IDisposable
    {
        private AiChatOperationGate? _gate = gate;

        public OperationKind Kind { get; } = kind;

        public void Dispose()
        {
            Interlocked.Exchange(ref _gate, null)?.Release(this);
        }
    }
}
