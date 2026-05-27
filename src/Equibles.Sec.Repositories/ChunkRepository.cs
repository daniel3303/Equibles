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

    public ChunkRepository(EquiblesFinancialDbContext dbContext)
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
        // Compose the text match and the ticker/document/type filters into one BM25
        // boolean query so ParadeDB resolves the filters INSIDE the index (with_index).
        // Layering them as SQL .Where(...) predicates instead made Postgres score every
        // text match first and post-filter the result on the heap (heap_filter) — for a
        // high-coverage ticker that scored set is enormous and blew the 5s budget (#2157).
        var clauses = new List<ParadeDbJsonQuery>
        {
            ParadeDbJsonQuery.Parse(searchText, lenient: true, conjunctionMode: true),
        };

        // Ticker (raw tokenizer) and DocumentType (single-token enum values) are stored
        // lowercased; Term is an exact, untokenized match, so the filter value must be
        // lowercased to line up with the indexed token. DocumentId is a UUID and matches
        // as-is.
        if (ticker != null)
            clauses.Add(ParadeDbJsonQuery.Term(nameof(Chunk.Ticker), ticker.ToLowerInvariant()));

        if (documentId.HasValue)
            clauses.Add(ParadeDbJsonQuery.Term(nameof(Chunk.DocumentId), documentId.Value));

        if (documentType != null)
            clauses.Add(
                ParadeDbJsonQuery.Term(
                    nameof(Chunk.DocumentType),
                    documentType.Value.ToLowerInvariant()
                )
            );

        var searchQuery = ParadeDbJsonQuery.Boolean(b => b.Must(clauses.ToArray())).ToJson();

        var query = DbContext.Set<Chunk>().Where(c => EF.Functions.JsonSearch(c.Id, searchQuery));

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
