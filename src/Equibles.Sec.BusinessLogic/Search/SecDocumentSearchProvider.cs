using Equibles.Search.Abstractions;

namespace Equibles.Sec.BusinessLogic.Search;

/// <summary>
/// SEC filings group. Reuses the production BM25 + semantic hybrid search and collapses chunk hits
/// to one entry per document, preserving relevance order.
/// </summary>
public class SecDocumentSearchProvider : ISearchProvider
{
    // Chunks are sub-document units; over-fetch so dedup-by-document usually still fills the group.
    // This is best-effort: if the top chunks all belong to few documents the group may under-fill.
    private const int ChunkOverFetchFactor = 4;

    private readonly HybridChunkSearcher _chunkSearcher;

    public SecDocumentSearchProvider(HybridChunkSearcher chunkSearcher)
    {
        _chunkSearcher = chunkSearcher;
    }

    public string Category => "SEC Filings";

    public int Order => 10;

    public async Task<SearchResultGroup> Search(
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

        var documents = chunks
            .GroupBy(chunk => chunk.DocumentId)
            .Select(group => group.First())
            .Take(request.MaxPerProvider)
            .ToList();

        return new SearchResultGroup
        {
            Category = Category,
            Order = Order,
            Hits = documents
                .Select(chunk => new SearchHit
                {
                    Title = $"{chunk.DocumentType.DisplayName} · {chunk.Ticker}",
                    Subtitle = chunk.ReportingDate.ToString("yyyy-MM-dd"),
                    Kind = "Filing",
                    Date = DateOnly.FromDateTime(chunk.ReportingDate),
                    RouteValues =
                    {
                        ["ticker"] = chunk.Ticker,
                        ["id"] = chunk.DocumentId.ToString(),
                    },
                })
                .ToList(),
        };
    }
}
