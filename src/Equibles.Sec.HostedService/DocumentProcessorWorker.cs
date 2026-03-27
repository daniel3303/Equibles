using Equibles.Errors.BusinessLogic;
using Equibles.Core;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.Sec.HostedService;

public class DocumentProcessorWorker : BackgroundService {
    private readonly ILogger<DocumentProcessorWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _intervalBetweenExecutions;

    public DocumentProcessorWorker(ILogger<DocumentProcessorWorker> logger, IServiceScopeFactory scopeFactory) {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _intervalBetweenExecutions = TimeSpan.FromSeconds(15);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            _logger.LogInformation("Document processor running at: {Time}", DateTimeOffset.Now);
            await DoWork(stoppingToken);

            GarbageCollectorUtil.ForceAggressiveCollection();
            await Task.Delay(_intervalBetweenExecutions, stoppingToken);
        }
    }

    private async Task DoWork(CancellationToken stoppingToken) {
        try {
            // Phase 1: Chunk ALL pending documents first (BM25 search works immediately)
            await ChunkAllPendingDocuments(stoppingToken);

            // Phase 2: Generate embeddings for all chunks without them
            await GenerateAllPendingEmbeddings(stoppingToken);
        } catch (Exception e) {
            _logger.LogCritical(e, "Error while executing {Worker}", nameof(DocumentProcessorWorker));
            await ReportError("DocumentProcessor.DoWork", e.Message, e.StackTrace);
        }
    }

    private async Task ChunkAllPendingDocuments(CancellationToken stoppingToken) {
        _logger.LogInformation("Phase 1: Chunking all pending documents");

        while (!stoppingToken.IsCancellationRequested) {
            using var scope = _scopeFactory.CreateScope();
            var documentManager = scope.ServiceProvider.GetRequiredService<DocumentManager>();

            var workDone = await documentManager.ChunkDocumentBatch(stoppingToken);
            if (!workDone) break;

            GarbageCollectorUtil.ForceAggressiveCollection();
        }

        _logger.LogInformation("Phase 1 complete: All documents chunked");
    }

    private async Task GenerateAllPendingEmbeddings(CancellationToken stoppingToken) {
        _logger.LogInformation("Phase 2: Generating all pending embeddings");

        while (!stoppingToken.IsCancellationRequested) {
            using var scope = _scopeFactory.CreateScope();
            var documentManager = scope.ServiceProvider.GetRequiredService<DocumentManager>();

            var workDone = await documentManager.GenerateEmbeddingBatch(stoppingToken);
            if (!workDone) break;

            GarbageCollectorUtil.ForceAggressiveCollection();
        }

        _logger.LogInformation("Phase 2 complete: All embeddings generated");
    }

    private async Task ReportError(string context, string message, string stackTrace, string requestSummary = null) {
        try {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var errorManager = scope.ServiceProvider.GetRequiredService<ErrorManager>();
            await errorManager.Create(ErrorSource.DocumentProcessor, context, message, stackTrace, requestSummary);
        } catch { }
    }
}
