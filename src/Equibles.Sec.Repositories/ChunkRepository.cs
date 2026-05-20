using Equibles.Data;
using Equibles.ParadeDB.EntityFrameworkCore;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Repositories;

public class ChunkRepository : BaseRepository<Chunk>
{
    // Hard ceiling for the BM25 query. SearchAggregator advertises a 5s
    // per-provider budget via cooperative cancellation, but pdb.parse and
    // pdb.score don't check the token mid-execution — without this Postgres
    // happily runs the chunk search for minutes after the aggregator has
    // already returned Empty, pinning the Npgsql connection (issue #1026).
    private const int HybridSearchCommandTimeoutSeconds = 5;

    public ChunkRepository(EquiblesDbContext dbContext)
        : base(dbContext) { }

    public async Task<List<Chunk>> HybridSearch(
        string searchText,
        int maxResults,
        string ticker = null,
        Guid? documentId = null,
        DocumentType documentType = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = DbContext
            .Set<Chunk>()
            .Where(c => EF.Functions.Parse(c.Id, searchText, lenient: true, conjunctionMode: true));

        if (ticker != null)
            query = query.Where(c => c.Ticker == ticker);

        if (documentId.HasValue)
            query = query.Where(c => c.DocumentId == documentId.Value);

        if (documentType != null)
            query = query.Where(c => c.DocumentType == documentType);

        if (startDate.HasValue)
        {
            var startUtc = DateTime.SpecifyKind(
                startDate.Value.ToDateTime(TimeOnly.MinValue),
                DateTimeKind.Utc
            );
            query = query.Where(c => c.ReportingDate >= startUtc);
        }

        if (endDate.HasValue)
        {
            var endUtc = DateTime.SpecifyKind(
                endDate.Value.ToDateTime(TimeOnly.MinValue),
                DateTimeKind.Utc
            );
            query = query.Where(c => c.ReportingDate <= endUtc);
        }

        // Set a hard CommandTimeout for this call so Postgres aborts the
        // statement independently of pdb.parse / pdb.score honouring the
        // cancellation token, then restore the prior value so other queries
        // sharing this DbContext are not affected.
        var originalTimeout = DbContext.Database.GetCommandTimeout();
        DbContext.Database.SetCommandTimeout(HybridSearchCommandTimeoutSeconds);
        try
        {
            return await query
                .OrderByDescending(c => EF.Functions.Score(c.Id))
                .Take(maxResults)
                .ToListAsync(cancellationToken);
        }
        finally
        {
            DbContext.Database.SetCommandTimeout(originalTimeout);
        }
    }
}
