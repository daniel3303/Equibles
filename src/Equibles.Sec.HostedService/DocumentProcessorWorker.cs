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
    // embeddings produced — server likely down"). A faulted cycle backs off ~15s (ErrorBackoff is
    // capped at SleepInterval), so this many consecutive faults is roughly five minutes of solid
    // outage — comfortably past a normal reload. Below it the faults are transient and stay out of
    // the Errors page; only a genuinely sustained outage records a single row. Live "is it down"
    // visibility comes from the System Health probe + activity feed, not these rows.
    protected override int ErrorReportThreshold => 20;

    // Backfill frontiers for the two phases. They live on the worker (a singleton) because the
    // DocumentManager is re-resolved from a fresh scope per batch; each hydrates from its
    // persisted BackfillState row on first use, so a process restart resumes at the frontier
    // instead of paying the unfloored corpus scan.
    private readonly BackfillCursor _chunkCursor = new("document-chunking");
    private readonly BackfillCursor _embeddingCursor = new("chunk-embedding");

    public DocumentProcessorWorker(
        ILogger<DocumentProcessorWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    )
        : base(logger, scopeFactory, errorReporter) { }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        Logger.LogInformation("Phase 1: Chunking all pending documents");
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var chunkScope = ScopeFactory.CreateAsyncScope();
            var documentManager = chunkScope.ServiceProvider.GetRequiredService<DocumentManager>();
            var workDone = await documentManager.ChunkDocumentBatch(_chunkCursor, stoppingToken);
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
