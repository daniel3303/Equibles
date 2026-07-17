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
        IReadOnlyCollection<string> excludeTickers = null,
        Guid? documentId = null,
        IReadOnlyCollection<DocumentType> documentTypes = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        bool conjunctive = true,
        CancellationToken cancellationToken = default
    )
    {
        // Compose the text match and the ticker/document/type filters into one BM25
        // boolean query so ParadeDB resolves the filters INSIDE the index (with_index).
        // Layering them as SQL .Where(...) predicates instead made Postgres score every
        // text match first and post-filter the result on the heap (heap_filter) — for a
        // high-coverage ticker that scored set is enormous and blew the 5s budget (#2157).
        //
        // conjunctive: true ANDs every query token (the precise default); false ORs them —
        // used as a recall fallback when the conjunctive pass starves (natural-language
        // queries where one non-matching word excludes every on-point chunk).
        var clauses = new List<ParadeDbJsonQuery>
        {
            ParadeDbJsonQuery.Parse(searchText, lenient: true, conjunctionMode: conjunctive),
        };

        // Ticker (raw tokenizer) and DocumentType (single-token enum values) are stored
        // lowercased; Term is an exact, untokenized match, so the filter value must be
        // lowercased to line up with the indexed token. DocumentId is a UUID and matches
        // as-is.
        if (ticker != null)
            clauses.Add(ParadeDbJsonQuery.Term(nameof(Chunk.Ticker), ticker.ToLowerInvariant()));

        if (documentId.HasValue)
            clauses.Add(ParadeDbJsonQuery.Term(nameof(Chunk.DocumentId), documentId.Value));

        // One type is a plain required term; several nest as a boolean of shoulds (a
        // boolean with only should clauses requires at least one to match), so "10-K or
        // 10-Q" still resolves inside the index.
        if (documentTypes is { Count: 1 })
            clauses.Add(
                ParadeDbJsonQuery.Term(
                    nameof(Chunk.DocumentType),
                    documentTypes.First().Value.ToLowerInvariant()
                )
            );
        else if (documentTypes is { Count: > 1 })
            clauses.Add(
                ParadeDbJsonQuery.Boolean(b =>
                    b.Should(
                        documentTypes
                            .Select(t =>
                                ParadeDbJsonQuery.Term(
                                    nameof(Chunk.DocumentType),
                                    t.Value.ToLowerInvariant()
                                )
                            )
                            .ToArray()
                    )
                )
            );

        var searchQuery = ParadeDbJsonQuery
            .Boolean(b =>
            {
                b.Must(clauses.ToArray());
                // Exclusion must live INSIDE the index too: dropping a dominant filer's
                // hits after scoring would silently shrink the result set instead of
                // refilling it with the next-best matches (a subject company can own
                // 90% of the top hits for its own flagship keyword).
                if (excludeTickers is { Count: > 0 })
                    b.MustNot(
                        excludeTickers
                            .Select(t =>
                                ParadeDbJsonQuery.Term(nameof(Chunk.Ticker), t.ToLowerInvariant())
                            )
                            .ToArray()
                    );
            })
            .ToJson();

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
