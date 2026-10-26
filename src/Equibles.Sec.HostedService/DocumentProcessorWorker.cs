using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Options;

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

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        // Chunks exist only to carry embeddings - DocumentPersistenceService drops them with the
        // note "their embeddings cascade at the DB level". With Embedding:Enabled=false nothing
        // will ever embed them, so building them is pure work: on a single 4-core host this
        // pipeline dominated the CPU, starved the fact-extraction sweep, and contributed to the
        // Npgsql connection-open failures that parked documents.
        //
        // Skipping is lossless and reversible: the chunking queue is exactly
        // "ChunkedAt IS NULL" (index IX_Document_PendingChunking), so turning embeddings back on
        // picks up the same backlog in the same order.
        await using (var probe = ScopeFactory.CreateAsyncScope())
        {
            var embeddingConfig = probe
                .ServiceProvider.GetRequiredService<IOptions<EmbeddingConfig>>()
                .Value;
            if (!embeddingConfig.Enabled)
            {
                Logger.LogInformation(
                    "Document processing skipped: Embedding:Enabled is false, so chunks would never be embedded."
                );
                return;
            }
        }

        Logger.LogInformation("Phase 1: Chunking all pending documents");
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var chunkScope = ScopeFactory.CreateAsyncScope();
            var documentManager = chunkScope.ServiceProvider.GetRequiredService<DocumentManager>();
            var workDone = await documentManager.ChunkDocumentBatch(stoppingToken);
            if (!workDone)
                break;
        }
        Logger.LogInformation("Phase 1 complete: All documents chunked");

        Logger.LogInformation("Phase 2: Generating all pending embeddings");
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var embedScope = ScopeFactory.CreateAsyncScope();
            var documentManager = embedScope.ServiceProvider.GetRequiredService<DocumentManager>();
            var workDone = await documentManager.GenerateEmbeddingBatch(
                _embeddingCursor,
                stoppingToken
            );
            if (!workDone)
                break;
        }
        Logger.LogInformation("Phase 2 complete: All embeddings generated");
    }
}
