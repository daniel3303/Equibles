using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.Repositories;
using Equibles.Core.AutoWiring;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// The ingested-filing ledger behind the congressional sync services. Every
/// cycle loads the already-ingested source ids per filing kind so the clients
/// skip re-downloading those filings, and records the newly handled ones only
/// after the cycle's data has been committed — a failed cycle therefore
/// re-fetches its filings instead of losing them.
/// </summary>
[Service]
public class CongressionalFilingLedger
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CongressionalFilingLedger(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// The filings this lane should skip. Rows written by an older parser
    /// generation than <paramref name="parserVersion"/> are deliberately left
    /// out so their filings re-download and re-parse with the fields the newer
    /// parser extracts — at most <paramref name="reprocessLimit"/> of them per
    /// cycle, newest filing first, so a version bump drip-feeds its backfill
    /// instead of re-fetching the whole archive at once. A lane that never
    /// raises its version passes 0 and sees every row as processed, exactly as
    /// before.
    /// </summary>
    public virtual async Task<IReadOnlySet<string>> GetProcessedSourceIds(
        CongressionalFilingKind kind,
        CancellationToken ct,
        int parserVersion = 0,
        int reprocessLimit = 0
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository =
            scope.ServiceProvider.GetRequiredService<CongressionalFilingRecordRepository>();
        var records = await repository
            .GetByKind(kind)
            .Select(r => new LedgerEntry(r.SourceId, r.FilingDate, r.ParserVersion))
            .ToListAsync(ct);

        return SelectProcessed(records, parserVersion, reprocessLimit);
    }

    /// <summary>
    /// Splits the ledger into the filings to skip and the ones to re-ingest.
    /// Rows at or above the current parser version are always skipped; stale
    /// rows are re-ingested newest-filing-first up to the cycle's quota, and the
    /// rest are skipped for now. The ordering is stable, so each cycle takes the
    /// next slice and the backfill converges instead of re-picking at random.
    ///
    /// Newest first is load-bearing, not a preference. A lane only fetches
    /// indexes inside its coverage window, so a stale row older than that
    /// window can never be re-downloaded — queueing it oldest-first would park
    /// un-fetchable rows at the head of every cycle's quota and starve the
    /// in-window filings behind them forever. Taking the newest first drains
    /// everything reachable and leaves the unreachable residue at the back,
    /// where it costs nothing. Widening the window is what makes that residue
    /// reachable.
    /// </summary>
    internal static HashSet<string> SelectProcessed(
        IReadOnlyCollection<LedgerEntry> records,
        int parserVersion,
        int reprocessLimit
    )
    {
        var current = records.Where(r => r.ParserVersion >= parserVersion).Select(r => r.SourceId);
        var deferred = records
            .Where(r => r.ParserVersion < parserVersion)
            .OrderByDescending(r => r.FilingDate)
            .ThenBy(r => r.SourceId, StringComparer.Ordinal)
            .Skip(Math.Max(reprocessLimit, 0))
            .Select(r => r.SourceId);

        return current.Concat(deferred).ToHashSet();
    }

    internal sealed record LedgerEntry(string SourceId, DateOnly FilingDate, int ParserVersion);

    public virtual async Task RecordProcessed(
        CongressionalFilingKind kind,
        IReadOnlyCollection<ProcessedFiling> filings,
        CancellationToken ct,
        int parserVersion = 0
    )
    {
        if (filings.Count == 0)
            return;

        // Dedupe by source id: an id repeated in one batch (e.g. a listing
        // page boundary shifting mid-search) would make the upsert's ON
        // CONFLICT hit the same row twice and abort the whole statement.
        var records = filings
            .GroupBy(f => f.SourceId)
            .Select(g => g.First())
            .Select(f => new CongressionalFilingRecord
            {
                Kind = kind,
                SourceId = f.SourceId,
                FilingDate = f.FilingDate,
                ItemCount = f.ItemCount,
                ParserVersion = parserVersion,
            })
            .ToList();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        // A re-parsed filing MUST update its row: leaving the stale version in
        // place would put the same filing back in the queue every cycle and the
        // backfill would never finish.
        await dbContext
            .Set<CongressionalFilingRecord>()
            .UpsertRange(records)
            .On(r => new { r.Kind, r.SourceId })
            .WhenMatched(
                (existing, incoming) =>
                    new CongressionalFilingRecord
                    {
                        FilingDate = incoming.FilingDate,
                        ItemCount = incoming.ItemCount,
                        ParserVersion = incoming.ParserVersion,
                    }
            )
            .RunAsync(ct);
    }
}
