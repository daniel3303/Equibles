namespace Equibles.CommonStocks.Data.Helpers;

/// <summary>
/// Serializes the worker's independent SEC-company and external-reference writers while they
/// reconcile <c>SecondaryTickers</c>. Both writers run in the one worker process; without this
/// gate, either can save a list computed before the other's authoritative subset changed.
/// </summary>
public static class CommonStockListingSyncLock
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<IDisposable> Acquire(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        return new Releaser();
    }

    private sealed class Releaser : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Gate.Release();
        }
    }
}
