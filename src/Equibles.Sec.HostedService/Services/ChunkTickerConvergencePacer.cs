namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// The pace of the chunk ticker sweep: one corpus comparison a day, its documents worked off a
/// bounded number of chunk rows a cycle. The chunk table carries the search index, and a burst of
/// writes to it shows up as search timeouts.
/// </summary>
internal sealed class ChunkTickerConvergencePacer
{
    internal const int MaxChunksPerCycle = 5_000;
    internal static readonly TimeSpan RescanInterval = TimeSpan.FromHours(24);

    // A comparison that failed is not repeated on the worker's fault backoff of a few minutes.
    internal static readonly TimeSpan FailedScanRetry = TimeSpan.FromHours(1);

    private readonly Queue<Guid> _pending = new();
    private DateTimeOffset? _nextScanAt;

    public int Pending => _pending.Count;

    public async Task<(int Documents, int Chunks)> RunCycle(
        Func<CancellationToken, Task<List<Guid>>> findStaleDocuments,
        Func<Guid, CancellationToken, Task<int>> restamp,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (_pending.Count == 0 && (_nextScanAt == null || now >= _nextScanAt))
        {
            _nextScanAt = now + FailedScanRetry;
            foreach (var documentId in await findStaleDocuments(cancellationToken))
                _pending.Enqueue(documentId);
            _nextScanAt = now + RescanInterval;
        }

        var documents = 0;
        var chunks = 0;
        // Taken off the queue first: a document whose update keeps failing must not hold back the
        // ones behind it, and the next comparison finds it again.
        while (chunks < MaxChunksPerCycle && _pending.TryDequeue(out var documentId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            chunks += await restamp(documentId, cancellationToken);
            documents++;
        }
        return (documents, chunks);
    }
}
