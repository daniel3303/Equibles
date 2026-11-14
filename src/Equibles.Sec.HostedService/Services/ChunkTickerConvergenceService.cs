using Equibles.Data;
using Equibles.Sec.Data.Models.Chunks;
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
    ChunkRepository chunkRepository,
    EquiblesFinancialDbContext dbContext
)
{
    // The comparison probes one chunk of every document: seconds on a warm cache, longer cold.
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Documents whose chunks carry another ticker than their issuer presents now, newest filing
    /// first. One probe of each document's first chunk: a document's chunks are stamped together.
    /// </summary>
    public async Task<List<Guid>> FindStaleDocuments(CancellationToken cancellationToken)
    {
        var originalTimeout = dbContext.Database.GetCommandTimeout();
        dbContext.Database.SetCommandTimeout(ScanTimeout);
        try
        {
            return await StaleDocuments().ToListAsync(cancellationToken);
        }
        finally
        {
            dbContext.Database.SetCommandTimeout(originalTimeout);
        }
    }

    internal IQueryable<Guid> StaleDocuments()
    {
        var chunks = chunkRepository.GetAll();
        return documentRepository
            .GetAll()
            .Where(document =>
                document.Issuer.Presentation.Listing.Ticker != null
                && document.Issuer.Presentation.Listing.Ticker.Length <= Chunk.TickerMaxLength
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
        // A symbol the chunk column cannot hold stays out, as it does when the document is chunked.
        if (string.IsNullOrWhiteSpace(ticker) || ticker.Length > Chunk.TickerMaxLength)
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
