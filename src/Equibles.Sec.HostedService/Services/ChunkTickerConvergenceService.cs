using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// A chunk carries the ticker its issuer presented when the chunk was cut, and every ticker-scoped
/// search arm filters on it. A ticker change, a new presentation listing or a document moved to
/// another issuer leaves the older chunks under the former symbol, out of reach of a search for
/// the current one; this re-stamps them.
/// </summary>
public class ChunkTickerConvergenceService(
    DocumentRepository documentRepository,
    ChunkRepository chunkRepository
)
{
    /// <summary>
    /// Documents whose chunks carry another ticker than their issuer presents now, newest filing
    /// first. One probe of each document's first chunk: a document's chunks are stamped together.
    /// </summary>
    public Task<List<Guid>> FindStaleDocuments(CancellationToken cancellationToken) =>
        StaleDocuments().ToListAsync(cancellationToken);

    internal IQueryable<Guid> StaleDocuments()
    {
        var chunks = chunkRepository.GetAll();
        return documentRepository
            .GetAll()
            .Where(document =>
                document.Issuer.Presentation.Listing.Ticker != null
                && chunks.Any(chunk =>
                    chunk.DocumentId == document.Id
                    && chunk.Index == 0
                    && (
                        chunk.Ticker == null
                        || chunk.Ticker != document.Issuer.Presentation.Listing.Ticker.ToUpper()
                    )
                )
            )
            .OrderByDescending(document => document.ReportingDate)
            .Select(document => document.Id);
    }

    /// <summary>
    /// Stamps one document's chunks with the ticker its issuer presents at this moment, and answers
    /// how many chunk rows it rewrote.
    /// </summary>
    public async Task<int> Restamp(Guid documentId, CancellationToken cancellationToken)
    {
        var ticker = await documentRepository
            .GetAll()
            .Where(document => document.Id == documentId)
            .Select(document => document.Issuer.Presentation.Listing.Ticker)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(ticker))
            return 0;

        // The same spelling DocumentProcessor writes: the vector arm compares it exactly.
        ticker = ticker.ToUpperInvariant();
        return await chunkRepository
            .GetAll()
            .Where(chunk =>
                chunk.DocumentId == documentId && (chunk.Ticker == null || chunk.Ticker != ticker)
            )
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(chunk => chunk.Ticker, ticker),
                cancellationToken
            );
    }
}
