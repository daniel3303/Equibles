using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;

namespace Equibles.Sec.HostedService;

public class DocumentProcessorWorker : BaseScraperWorker
{
    protected override string WorkerName => "Document processor";
    protected override TimeSpan SleepInterval => TimeSpan.FromSeconds(15);
    protected override ErrorSource ErrorSource => ErrorSource.DocumentProcessor;

    // The embedding sidecar (vLLM) is recycled by autoheal and on every deploy, and then reloads
    // its model for a minute or two. During that window every embedding cycle faults ("no
    // embeddings produced — server likely down"); an all-fail chunking batch (blob store
    // unreachable) faults the same way. A faulted cycle backs off ~15s (ErrorBackoff is
    // capped at SleepInterval), so this many consecutive faults is roughly five minutes of solid
    // outage — comfortably past a normal reload. Below it the faults are transient and stay out of
    // the Errors page; only a genuinely sustained outage records a single row. Live "is it down"
    // visibility comes from the System Health probe + activity feed, not these rows.
    protected override int ErrorReportThreshold => 20;

    // The embedding frontier lives on the worker (a singleton) because the DocumentManager is
    // re-resolved from a fresh scope per batch. Chunking no longer needs a frontier: its partial
    // pending index makes every FIFO lookup bounded by the actual backlog.
    private readonly BackfillCursor _embeddingCursor = new("chunk-embedding");

    public DocumentProcessorWorker(
        ILogger<DocumentProcessorWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    )
        : base(logger, scopeFactory, errorReporter) { }

    // A text is in search once it is chunked; its embeddings only add the vector arm. Pending
    // documents are therefore chunked before every embedding batch: generating all embeddings
    // first kept new filings out of search for as long as the embedding backlog lasted, hours
    // during a corpus-wide text rebuild. The cycle ends when no embedding is left to generate.
    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ChunkPending(stoppingToken);
            if (stoppingToken.IsCancellationRequested || !await EmbedBatch(stoppingToken))
                break;
        }
    }

    private async Task ChunkPending(CancellationToken stoppingToken)
    {
        var chunked = true;
        while (chunked && !stoppingToken.IsCancellationRequested)
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var documentManager = scope.ServiceProvider.GetRequiredService<DocumentManager>();
            chunked = await documentManager.ChunkDocumentBatch(stoppingToken);
        }
    }

    private async Task<bool> EmbedBatch(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var documentManager = scope.ServiceProvider.GetRequiredService<DocumentManager>();
        return await documentManager.GenerateEmbeddingBatch(_embeddingCursor, stoppingToken);
    }
}
