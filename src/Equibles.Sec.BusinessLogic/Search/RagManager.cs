using System.Text;
using Equibles.Core.AutoWiring;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.CommonStocks.Repositories;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.BusinessLogic.Search;

[Service(ServiceLifetime.Scoped, typeof(IRagManager))]
public class RagManager : IRagManager {
    private readonly ChunkRepository _chunkRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly ILogger<RagManager> _logger;

    public RagManager(ChunkRepository chunkRepository,
        CommonStockRepository commonStockRepository,
        ILogger<RagManager> logger
    ) {
        _chunkRepository = chunkRepository;
        _commonStockRepository = commonStockRepository;
        _logger = logger;
    }

    public async Task<List<Chunk>> SearchRelevantChunks(string query, int maxResults = 5, DocumentType documentType = null,
        DateOnly? startDate = null, DateOnly? endDate = null
    ) {
        var chunks = await _chunkRepository.HybridSearch(
            query,
            maxResults,
            documentType: documentType,
            startDate: startDate,
            endDate: endDate
        );

        _logger.LogInformation("Found {Count} relevant chunks for query", chunks.Count);
        return chunks;
    }

    public async Task<List<Chunk>> SearchRelevantChunksByCompany(string query, string ticker, int maxResults = 5,
        DocumentType documentType = null, DateOnly? startDate = null, DateOnly? endDate = null
    ) {
        ticker = await ResolvePrimaryTicker(ticker);
        var chunks = await _chunkRepository.HybridSearch(
            query,
            maxResults,
            ticker,
            documentType: documentType,
            startDate: startDate,
            endDate: endDate
        );

        _logger.LogInformation("Found {Count} relevant chunks for company {Ticker}", chunks.Count, ticker);
        return chunks;
    }

    public async Task<List<Chunk>> SearchRelevantChunksByDocument(string query, Guid documentId, int maxResults = 5) {
        var chunks = await _chunkRepository.HybridSearch(
            query,
            maxResults,
            documentId: documentId
        );

        _logger.LogInformation("Found {Count} relevant chunks for document {DocumentId}", chunks.Count, documentId);
        return chunks;
    }

    public async Task<List<Chunk>> SearchRelevantChunksByDocumentType(string query, DocumentType documentType,
        int maxResults = 5
    ) {
        return await SearchRelevantChunks(query, maxResults, documentType);
    }

    public Task<string> BuildContext(List<Chunk> chunks) {
        if (!chunks.Any()) {
            return Task.FromResult("No relevant financial documents found.");
        }

        var context = new StringBuilder();
        context.AppendLine("Relevant financial document excerpts:");
        context.AppendLine();

        var groupedChunks = chunks.GroupBy(c => new {
            c.Document.CommonStock.Ticker,
            c.Document.DocumentType,
            c.Document.ReportingDate
        });

        foreach (var group in groupedChunks) {
            var firstChunk = group.First();
            context.AppendLine($"## {firstChunk.Document.CommonStock.Name} ({group.Key.Ticker})");
            context.AppendLine($"**Document:** {group.Key.DocumentType} filed on {group.Key.ReportingDate}");
            context.AppendLine();

            foreach (var chunk in group.OrderBy(c => c.StartPosition)) {
                if (!string.IsNullOrWhiteSpace(chunk.Content)) {
                    context.AppendLine(chunk.StartLineNumber > 0
                        ? $"**Excerpt {chunk.Index + 1} (line ~{chunk.StartLineNumber:N0}):**"
                        : $"**Excerpt {chunk.Index + 1}:**");
                    context.AppendLine(chunk.Content);
                    context.AppendLine();
                }
            }

            context.AppendLine("---");
            context.AppendLine();
        }

        return Task.FromResult(context.ToString());
    }

    private async Task<string> ResolvePrimaryTicker(string ticker) {
        var stock = await _commonStockRepository.GetByTicker(ticker);
        return stock?.Ticker ?? ticker;
    }
}
