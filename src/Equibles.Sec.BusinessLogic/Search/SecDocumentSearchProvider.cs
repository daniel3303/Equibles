using Equibles.Search.Abstractions;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.BusinessLogic.Search;

/// <summary>
/// SEC filings group. When the query is an exact ticker it lists that company's most recent
/// filings (newest first); otherwise it reuses the production BM25 + semantic hybrid search and
/// collapses chunk hits to one entry per document, preserving relevance order.
/// </summary>
public class SecDocumentSearchProvider : ISearchProvider
{
    // Chunks are sub-document units; over-fetch so dedup-by-document usually still fills the group.
    // This is best-effort: if the top chunks all belong to few documents the group may under-fill.
    private const int ChunkOverFetchFactor = 4;

    private readonly HybridChunkSearcher _chunkSearcher;
    private readonly DocumentRepository _documentRepository;

    public SecDocumentSearchProvider(
        HybridChunkSearcher chunkSearcher,
        DocumentRepository documentRepository
    )
    {
        _chunkSearcher = chunkSearcher;
        _documentRepository = documentRepository;
    }

    public string Category => "SEC Filings";

    public int Order => 10;

    public async Task<SearchResultGroup> Search(
        SearchRequest request,
        CancellationToken cancellationToken
    )
    {
        // A typed ticker ("ARE") means "this company's filings" — surface its most RECENT ones, not
        // chunks that merely contain the token. A content search for a short, common ticker word
        // ("are") otherwise ranks old filings heavy on that word and buries this year's filings.
        var hits = await SearchRecentFilingsByTicker(request, cancellationToken);
        if (hits.Count == 0)
        {
            hits = await SearchByContent(request, cancellationToken);
        }

        return new SearchResultGroup
        {
            Category = Category,
            Order = Order,
            Hits = hits,
        };
    }

    // The exact-ticker path: the company's newest filings first, within any requested date window.
    // Returns an empty list when the query isn't a ticker, so the caller falls back to content search.
    private async Task<List<SearchHit>> SearchRecentFilingsByTicker(
        SearchRequest request,
        CancellationToken cancellationToken
    )
    {
        var ticker = request.Query.Trim();
        if (string.IsNullOrEmpty(ticker))
        {
            return [];
        }

        var query = _documentRepository.GetByTicker(ticker);
        if (request.DateFrom.HasValue)
        {
            query = query.Where(document => document.ReportingDate >= request.DateFrom.Value);
        }
        if (request.DateTo.HasValue)
        {
            query = query.Where(document => document.ReportingDate <= request.DateTo.Value);
        }

        var documents = await query
            .OrderByDescending(document => document.ReportingDate)
            .Take(request.MaxPerProvider)
            .ToListAsync(cancellationToken);

        return documents.Select(ProjectDocument).ToList();
    }

    // The free-text path: hybrid BM25 + semantic over chunk content, collapsed to one entry per
    // document in relevance order.
    private async Task<List<SearchHit>> SearchByContent(
        SearchRequest request,
        CancellationToken cancellationToken
    )
    {
        var chunks = await _chunkSearcher.Search(
            request.Query,
            request.MaxPerProvider * ChunkOverFetchFactor,
            startDate: request.DateFrom,
            endDate: request.DateTo,
            cancellationToken: cancellationToken
        );

        var documentChunks = chunks
            .GroupBy(chunk => chunk.DocumentId)
            .Select(group => group.First())
            .Take(request.MaxPerProvider)
            .ToList();

        if (documentChunks.Count == 0)
        {
            return [];
        }

        // Hybrid search materializes chunks without their parent documents. Load the authoritative
        // filing dates in one batch: dereferencing chunk.Document here would issue one synchronous
        // lazy-load query per hit and can exhaust the global search provider's five-second budget.
        var documentIds = documentChunks.Select(chunk => chunk.DocumentId).ToList();
        var reportingDates = await _documentRepository
            .GetAll()
            .Where(document => documentIds.Contains(document.Id))
            .Select(document => new { document.Id, document.ReportingDate })
            .ToDictionaryAsync(
                document => document.Id,
                document => document.ReportingDate,
                cancellationToken
            );

        return documentChunks
            .Select(chunk => ProjectChunk(chunk, reportingDates[chunk.DocumentId]))
            .ToList();
    }

    private static SearchHit ProjectDocument(Document document) =>
        new()
        {
            Title = $"{document.DocumentType.DisplayName} · {document.CommonStock.Ticker}",
            Subtitle = document.ReportingDate.ToString("yyyy-MM-dd"),
            Kind = "Filing",
            Date = document.ReportingDate,
            RouteValues =
            {
                ["ticker"] = document.CommonStock.Ticker,
                ["id"] = document.Id.ToString(),
            },
        };

    private static SearchHit ProjectChunk(Chunk chunk, DateOnly reportingDate) =>
        new()
        {
            Title = $"{chunk.DocumentType.DisplayName} · {chunk.Ticker}",
            Subtitle = reportingDate.ToString("yyyy-MM-dd"),
            Kind = "Filing",
            Date = reportingDate,
            RouteValues = { ["ticker"] = chunk.Ticker, ["id"] = chunk.DocumentId.ToString() },
        };
}
