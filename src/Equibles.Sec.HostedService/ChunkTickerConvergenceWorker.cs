using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Runs <see cref="ChunkTickerConvergenceService"/>. Each cycle rewrites a bounded number of chunk
/// rows and never chains into the next: the chunk table carries the search index, and a burst of
/// writes to it shows up as search timeouts. The whole corpus is compared once a day; the documents
/// that comparison found are worked off in between.
/// </summary>
public class ChunkTickerConvergenceWorker : BaseScraperWorker
{
    internal const int MaxChunksPerCycle = 5_000;
    internal static readonly TimeSpan RescanInterval = TimeSpan.FromHours(24);

    private readonly TimeProvider _timeProvider;
    private readonly Queue<Guid> _pending = new();
    private DateTimeOffset? _scannedAt;

    protected override string WorkerName => "Chunk ticker convergence";
    protected override TimeSpan SleepInterval => TimeSpan.FromMinutes(5);
    protected override ErrorSource ErrorSource => ErrorSource.DocumentProcessor;

    // Behind the filing scraper and the EDGAR backfills, which start within 15 minutes.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(20);

    public ChunkTickerConvergenceWorker(
        ILogger<ChunkTickerConvergenceWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        TimeProvider timeProvider
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _timeProvider = timeProvider;
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ChunkTickerConvergenceService>();
        var now = _timeProvider.GetUtcNow();
        if (_pending.Count == 0 && (_scannedAt == null || now - _scannedAt >= RescanInterval))
        {
            foreach (var documentId in await service.FindStaleDocuments(stoppingToken))
                _pending.Enqueue(documentId);
            _scannedAt = now;
            if (_pending.Count > 0)
                Logger.LogInformation(
                    "Chunk ticker convergence: {Documents} document(s) carry a former ticker",
                    _pending.Count
                );
        }

        var documents = 0;
        var rewritten = 0;
        while (rewritten < MaxChunksPerCycle && _pending.TryPeek(out var documentId))
        {
            stoppingToken.ThrowIfCancellationRequested();
            rewritten += await service.Restamp(documentId, stoppingToken);
            _pending.Dequeue();
            documents++;
        }
        if (documents > 0)
            Logger.LogInformation(
                "Chunk ticker convergence cycle: {Documents} document(s), {Chunks} chunk(s) re-stamped, {Pending} document(s) left",
                documents,
                rewritten,
                _pending.Count
            );
    }
}
