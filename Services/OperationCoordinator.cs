namespace MediaStudio.Services;

// Shared by the tool view-models and maintenance actions. Leases cover cleanup as
// well as visible busy state, so cancellation cannot prematurely unlock updates.
public sealed class OperationCoordinator
{
    private readonly object _sync = new();
    private int _workers;
    private bool _maintenance;
    public bool IsMaintenance { get { lock (_sync) return _maintenance; } }
    public bool TryBeginWork(out IDisposable lease) => TryBegin(false, out lease);
    public bool TryBeginMaintenance(out IDisposable lease) => TryBegin(true, out lease);
    private bool TryBegin(bool exclusive, out IDisposable lease)
    {
        lock (_sync)
        {
            if (_maintenance || (exclusive && _workers != 0)) { lease = null!; return false; }
            if (exclusive) _maintenance = true; else _workers++;
            lease = new Lease(this, exclusive);
            return true;
        }
    }
    private sealed class Lease(OperationCoordinator owner, bool exclusive) : IDisposable
    {
        private OperationCoordinator? _owner = owner;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current == null) return;
            lock (current._sync) { if (exclusive) current._maintenance = false; else current._workers--; }
        }
    }
}
