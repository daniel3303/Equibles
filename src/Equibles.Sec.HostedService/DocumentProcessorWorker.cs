using Equibles.Core;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;

namespace Equibles.Sec.HostedService;

public class DocumentProcessorWorker : BaseScraperWorker {
    protected override string WorkerName => "Document processor";
    protected override TimeSpan SleepInterval => TimeSpan.FromSeconds(15);
    protected override ErrorSource ErrorSource => ErrorSource.DocumentProcessor;

    public DocumentProcessorWorker(
        ILogger<DocumentProcessorWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    ) : base(logger, scopeFactory, errorReporter) { }

    protected override async Task DoWork(CancellationToken stoppingToken) {
        Logger.LogInformation("Phase 1: Chunking all pending documents");
        while (!stoppingToken.IsCancellationRequested) {
            using var chunkScope = ScopeFactory.CreateScope();
            var documentManager = chunkScope.ServiceProvider.GetRequiredService<DocumentManager>();
            var workDone = await documentManager.ChunkDocumentBatch(stoppingToken);
            if (!workDone) break;
            GarbageCollectorUtil.ForceAggressiveCollection();
        }
        Logger.LogInformation("Phase 1 complete: All documents chunked");

        Logger.LogInformation("Phase 2: Generating all pending embeddings");
        while (!stoppingToken.IsCancellationRequested) {
            using var embedScope = ScopeFactory.CreateScope();
            var documentManager = embedScope.ServiceProvider.GetRequiredService<DocumentManager>();
            var workDone = await documentManager.GenerateEmbeddingBatch(stoppingToken);
            if (!workDone) break;
            GarbageCollectorUtil.ForceAggressiveCollection();
        }
        Logger.LogInformation("Phase 2 complete: All embeddings generated");
    }
}
