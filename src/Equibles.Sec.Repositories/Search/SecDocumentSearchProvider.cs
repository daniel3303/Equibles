using Equibles.Search.Abstractions;

namespace Equibles.Sec.Repositories.Search;

/// <summary>
/// SEC filings group. Reuses the production BM25 + vector hybrid search and collapses chunk hits
/// to one entry per document, preserving relevance order.
/// </summary>
public class SecDocumentSearchProvider : ISearchProvider
{
    // Chunks are sub-document units; over-fetch so dedup-by-document usually still fills the group.
    // This is best-effort: if the top chunks all belong to few documents the group may under-fill.
    private const int ChunkOverFetchFactor = 4;

    private readonly ChunkRepository _chunkRepository;

    public SecDocumentSearchProvider(ChunkRepository chunkRepository)
    {
        _chunkRepository = chunkRepository;
    }

    public string Category => "SEC Filings";

    public int Order => 10;

    public async Task<SearchResultGroup> Search(
        SearchRequest request,
        CancellationToken cancellationToken
    )
    {
        var chunks = await _chunkRepository.HybridSearch(
            request.Query,
            request.MaxPerProvider * ChunkOverFetchFactor,
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
