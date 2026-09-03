using System.Diagnostics;
using Equibles.Data;
using Equibles.ParadeDB.EntityFrameworkCore;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Equibles.Sec.Repositories;

public class ChunkRepository : BaseRepository<Chunk>
{
    // Hard ceiling for the BM25 query. SearchAggregator advertises a 5s
    // per-provider budget via cooperative cancellation, but pdb.parse and
    // pdb.score don't check the token mid-execution — without this Postgres
    // happily runs the chunk search for minutes after the aggregator has
    // already returned Empty, pinning the Npgsql connection (issue #1026).
    private const int HybridSearchCommandTimeoutSeconds = 5;
    private const int ScopedFallbackCommandTimeoutSeconds = 3;

    public ChunkRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    // virtual: unit tests stub the search seam by subclassing (no BM25 index in a unit run).
    // commandTimeoutSeconds overrides the default statement budget for this one call — the
    // searcher tightens it on a degrade pass that follows an already timed-out pass.
    public virtual async Task<List<Chunk>> HybridSearch(
        string searchText,
        int maxResults,
        string ticker = null,
        IReadOnlyCollection<string> excludeTickers = null,
        Guid? documentId = null,
        IReadOnlyCollection<DocumentType> documentTypes = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        bool conjunctive = true,
        int? commandTimeoutSeconds = null,
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

        // Document.ReportingDate is the filing/source date surfaced everywhere else. The chunk's
        // denormalized copy is only an indexed cache and legacy transcript chunks can trail a
        // corrected document date, so it must never decide date-window membership (#7049).
        if (startDate is { } windowStart)
            query = query.Where(c => c.Document.ReportingDate >= windowStart);

        if (endDate is { } windowEnd)
            query = query.Where(c => c.Document.ReportingDate <= windowEnd);

        // Set a hard CommandTimeout for this call so Postgres aborts the
        // statement independently of pdb.parse / pdb.score honouring the
        // cancellation token, then restore the prior value so other queries
        // sharing this DbContext are not affected.
        var timeoutSeconds = commandTimeoutSeconds ?? HybridSearchCommandTimeoutSeconds;
        var originalTimeout = DbContext.Database.GetCommandTimeout();
        DbContext.Database.SetCommandTimeout(timeoutSeconds);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await query
                .OrderByDescending(c => EF.Functions.Score(c.Id))
                .Take(maxResults)
                .ToListAsync(cancellationToken);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException
                && !cancellationToken.IsCancellationRequested
                && IsStatementTimeout(exception, stopwatch.Elapsed, timeoutSeconds)
            )
        {
            // The hard CommandTimeout above fired (a cold BM25 index after a Postgres
            // restart can push a long multi-term query past the budget). Surface it as a
            // typed timeout so callers can degrade — run another pass, let the vector arm
            // answer — instead of failing the whole search, and so an empty result is
            // never conflated with "no matches".
            throw new ChunkSearchTimeoutException(
                $"BM25 chunk search exceeded its {timeoutSeconds}s statement budget.",
                exception
            );
        }
        finally
        {
            DbContext.Database.SetCommandTimeout(originalTimeout);
        }
    }

    // Bounded degrade for a SCOPED search whose ParadeDB pass timed out. A ticker or a document id
    // narrows this PostgreSQL full-text scan to one bounded slice before Content is parsed - the
    // Chunk ticker btree, or the unique DocumentId+Index btree - so a cold or contended BM25 index
    // can still return proven matches instead of a 500. Document scope earns the same degrade as
    // ticker scope and is strictly cheaper: one filing's chunks are a far smaller slice than a
    // large filer's whole corpus. It is never used for an UNSCOPED search, because generating
    // tsvectors over the whole Chunk table would recreate the very unbounded work this degrade
    // exists to avoid - hence the refusal in BuildScopedFallbackQuery rather than a silent scan.
    public virtual async Task<List<Chunk>> HybridSearchScopedFallback(
        string searchText,
        int maxResults,
        string ticker = null,
        Guid? documentId = null,
        IReadOnlyCollection<DocumentType> documentTypes = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken cancellationToken = default
    )
    {
        var originalTimeout = DbContext.Database.GetCommandTimeout();
        DbContext.Database.SetCommandTimeout(ScopedFallbackCommandTimeoutSeconds);
        try
        {
            return await BuildScopedFallbackQuery(
                    searchText,
                    maxResults,
                    ticker,
                    documentId,
                    documentTypes,
                    startDate,
                    endDate
                )
                .ToListAsync(cancellationToken);
        }
        finally
        {
            DbContext.Database.SetCommandTimeout(originalTimeout);
        }
    }

    internal IQueryable<Chunk> BuildScopedFallbackQuery(
        string searchText,
        int maxResults,
        string ticker = null,
        Guid? documentId = null,
        IReadOnlyCollection<DocumentType> documentTypes = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null
    )
    {
        // The scope is the whole safety argument for this query, so an unscoped call is refused
        // rather than served: without a ticker or a document id PostgreSQL would build tsvectors
        // over every chunk in the corpus, which is the unbounded work the BM25 budget already
        // failed on. Pinned by a unit test - a comment cannot enforce it.
        if (string.IsNullOrWhiteSpace(ticker) && !documentId.HasValue)
            throw new ArgumentException(
                "The full-text fallback requires a ticker or a document id; an unscoped scan would "
                    + "build tsvectors over the whole Chunk table.",
                nameof(ticker)
            );

        IQueryable<Chunk> query = DbContext.Set<Chunk>();

        // Narrow to the scope FIRST so the btree cuts the row set before Content is parsed. Both
        // filters apply when both are supplied; either alone is enough to bound the scan.
        if (!string.IsNullOrWhiteSpace(ticker))
        {
            var normalizedTicker = ticker.ToUpperInvariant();
            query = query.Where(c => c.Ticker == normalizedTicker);
        }

        if (documentId.HasValue)
            query = query.Where(c => c.DocumentId == documentId.Value);

        query = query.Where(c =>
            EF.Functions.ToTsVector("english", c.Content)
                .Matches(EF.Functions.WebSearchToTsQuery("english", searchText))
        );

        if (documentTypes is { Count: > 0 })
        {
            var types = documentTypes.ToList();
            query = query.Where(c => types.Contains(c.DocumentType));
        }

        // Match the primary search path's filing-date source of truth; the denormalized chunk
        // date can trail a corrected parent document date.
        if (startDate is { } windowStart)
            query = query.Where(c => c.Document.ReportingDate >= windowStart);
        if (endDate is { } windowEnd)
            query = query.Where(c => c.Document.ReportingDate <= windowEnd);

        return query
            .OrderByDescending(c => c.Document.ReportingDate)
            .ThenBy(c => c.StartPosition)
            .Take(maxResults);
    }

    // How far past the statement budget an elapsed run may land and still be attributed to
    // it (the cancel round-trip adds a moment). Anything slower is some other wait.
    private const int StatementTimeoutSlackSeconds = 2;

    // Npgsql surfaces its CommandTimeout-triggered cancellation either as the raw backend
    // error (PostgresException 57014 "canceling statement due to user request") or wrapped
    // in an NpgsqlException with a TimeoutException inside, depending on where in the read
    // loop the cancel lands — walk the chain and match both shapes. The bare-TimeoutException
    // shape is only trusted when the run actually lasted about the statement budget: a pool
    // exhaustion or connect timeout carries the same TimeoutException but elapses on the
    // connection-string timeout (15s default), and relabelling THAT as the statement budget
    // would send the caller into a doomed degrade pass against a database it cannot reach.
    // A caller-requested cancellation surfaces as OperationCanceledException (or trips the
    // token) and is excluded at the catch site.
    // internal: the classification rules are pinned by unit tests.
    internal static bool IsStatementTimeout(
        Exception exception,
        TimeSpan elapsed,
        int budgetSeconds
    )
    {
        var withinBudgetWindow =
            elapsed <= TimeSpan.FromSeconds(budgetSeconds + StatementTimeoutSlackSeconds);
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.QueryCanceled })
                return true;
            if (current is TimeoutException && withinBudgetWindow)
                return true;
        }

        return false;
    }
}
