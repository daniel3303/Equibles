using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Recomputes the per-quarter AUM and sector-allocation snapshots that power
/// /holdings/stats and /holdings/trends.
///
/// The live multi-distinct GROUP BY on InstitutionalHoldings cannot finish
/// inside the 30s Npgsql command timeout at production scale (~5-15M rows per
/// quarter * ~100 quarters). This service filters by a single ReportDate
/// first, so the existing <c>[Index(ReportDate)]</c> btree narrows the scan
/// to one quarter's slice and the multi-distinct only ever sees that slice.
///
/// Entry points:
/// <list type="bullet">
///   <item><c>RebuildQuarterAsync</c> — single quarter, bounded work; called
///     by the drain worker for each quarter whose <c>DirtyAt</c> cooldown has
///     elapsed.</item>
///   <item><c>RebuildRecentAsync</c> — top-N most recent quarters; called by
///     the daily safety-net worker. Bounded cost regardless of how many
///     historical quarters the table holds.</item>
///   <item><c>RebuildAllAsync</c> — enumerates every distinct ReportDate;
///     called by the one-time first-boot backfill. Each quarter is wrapped in
///     its own try/catch so a single bad quarter (e.g. a unique-key collision
///     from a parallel consumer rebuild) doesn't abort the whole pass.</item>
/// </list>
///
/// Writes go through FlexLabs <c>UpsertRange</c> (single <c>INSERT … ON
/// CONFLICT</c>) and <c>ExecuteDeleteAsync</c> (single <c>DELETE</c>) so the
/// consumer and the safety-net worker can rebuild the same ReportDate
/// concurrently without a TOCTOU race between "load existing → mutate → save".
/// </summary>
[Service]
public class HoldingsAggregateRefreshService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HoldingsAggregateRefreshService> _logger;

    public HoldingsAggregateRefreshService(
        IServiceScopeFactory scopeFactory,
        ILogger<HoldingsAggregateRefreshService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public virtual Task RebuildQuarterAsync(
        DateOnly reportDate,
        CancellationToken cancellationToken
    ) => RebuildQuarterAsync(reportDate, commandTimeout: null, cancellationToken);

    public virtual async Task RebuildQuarterAsync(
        DateOnly reportDate,
        TimeSpan? commandTimeout,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        await RebuildQuarterInScope(
            scope.ServiceProvider,
            reportDate,
            commandTimeout,
            cancellationToken
        );
    }

    public Task RebuildAllAsync(CancellationToken cancellationToken) =>
        RebuildAllAsync(commandTimeout: null, cancellationToken);

    public async Task RebuildAllAsync(TimeSpan? commandTimeout, CancellationToken cancellationToken)
    {
        var reportDates = await LoadReportDates(
            query => query.OrderBy(d => d),
            commandTimeout,
            cancellationToken
        );
        await RebuildReportDates(reportDates, commandTimeout, cancellationToken);
    }

    public Task RebuildRecentAsync(int quarters, CancellationToken cancellationToken) =>
        RebuildRecentAsync(quarters, commandTimeout: null, cancellationToken);

    public async Task RebuildRecentAsync(
        int quarters,
        TimeSpan? commandTimeout,
        CancellationToken cancellationToken
    )
    {
        if (quarters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quarters),
                "Must rebuild at least one quarter."
            );
        }

        // Take the N most recent ReportDate values. Bounded cost regardless of
        // how many historical quarters exist — historical snapshots only refresh
        // when the consumer marks them dirty (or via the first-boot backfill).
        var reportDates = await LoadReportDates(
            query => query.OrderByDescending(d => d).Take(quarters),
            commandTimeout,
            cancellationToken
        );
        await RebuildReportDates(reportDates, commandTimeout, cancellationToken);
    }

    private async Task<List<DateOnly>> LoadReportDates(
        Func<IQueryable<DateOnly>, IQueryable<DateOnly>> orderAndLimit,
        TimeSpan? commandTimeout,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        if (commandTimeout is not null)
        {
            dbContext.Database.SetCommandTimeout(commandTimeout.Value);
        }

        // 13F quarter ends only: Schedule 13D/G rows carry per-day event dates,
        // so without the filter the "N most recent report dates" the daily
        // safety net rebuilds are the last N business days of 13D/G activity —
        // not the recent 13F quarters it exists to protect.
        var distinctDates = dbContext
            .Set<InstitutionalHolding>()
            .Where(h => h.FilingType == FilingType.Form13F)
            .Select(h => h.ReportDate)
            .Distinct();
        return await orderAndLimit(distinctDates).ToListAsync(cancellationToken);
    }

    private async Task RebuildReportDates(
        List<DateOnly> reportDates,
        TimeSpan? commandTimeout,
        CancellationToken cancellationToken
    )
    {
        _logger.LogInformation(
            "Rebuilding holdings aggregate snapshots for {Count} quarter(s)",
            reportDates.Count
        );

        var failed = 0;
        foreach (var reportDate in reportDates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                await RebuildQuarterInScope(
                    scope.ServiceProvider,
                    reportDate,
                    commandTimeout,
                    cancellationToken
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Defence in depth on top of the race-free upsert path. If one
                // quarter throws (transient DB failure, schema drift, etc.),
                // log and keep going — the rest of the snapshot table is still
                // worth refreshing this cycle.
                failed++;
                _logger.LogError(
                    ex,
                    "Failed to rebuild holdings aggregate snapshot for {ReportDate}; continuing with remaining quarters",
                    reportDate
                );
            }
        }

        if (failed > 0)
        {
            _logger.LogWarning(
                "Holdings aggregate snapshot rebuild finished with {Failed}/{Total} quarter(s) failed",
                failed,
                reportDates.Count
            );
        }
    }

    private async Task RebuildQuarterInScope(
        IServiceProvider services,
        DateOnly reportDate,
        TimeSpan? commandTimeout,
        CancellationToken cancellationToken
    )
    {
        var dbContext = services.GetRequiredService<EquiblesFinancialDbContext>();
        if (commandTimeout is not null)
        {
            dbContext.Database.SetCommandTimeout(commandTimeout.Value);
        }

        await UpsertAumSnapshot(dbContext, reportDate, cancellationToken);
        await UpsertSectorSnapshots(dbContext, reportDate, cancellationToken);
        await UpsertStockActivitySnapshots(dbContext, reportDate, cancellationToken);
        await UpsertHolderSnapshots(dbContext, reportDate, cancellationToken);
        await RefreshCombinedLane(dbContext, reportDate, cancellationToken);
    }

    // Maintains the open filing window's combined-lane snapshot
    // (StockQuarterlyActivityCombined). While the newest quarter's 45-day window is
    // open its market-wide view must carry non-filers forward at their prior-quarter
    // positions; the live combined aggregation costs ~30s+ (GROUP BY over two quarters
    // plus correlated NOT-EXISTS probes), so it runs HERE — once per dirty-quarter
    // drain — and consumers read the materialised rows. Rebuilt when either side of
    // the comparison changes (the open quarter's own rebuild, or the prior quarter's —
    // the carry-forward reads prior-quarter rows, so an amendment there shifts the
    // combined view too). When the window is closed the lane is retired outright: the
    // plain snapshot is authoritative and a stale carry-forward row must never
    // survive into the closed quarter (the daily safety net rebuilds the newest
    // quarters, giving retirement a reliable trigger).
    private async Task RefreshCombinedLane(
        EquiblesFinancialDbContext dbContext,
        DateOnly rebuiltReportDate,
        CancellationToken cancellationToken
    )
    {
        var latest = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h => h.FilingType == FilingType.Form13F)
            .Select(h => h.ReportDate)
            .OrderByDescending(d => d)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest == default)
        {
            return;
        }

        if (!CombinedQuarterHelper.IsFilingWindowOpen(latest))
        {
            await dbContext
                .Set<StockQuarterlyActivityCombined>()
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var previous = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h => h.ReportDate < latest && h.FilingType == FilingType.Form13F)
            .Select(h => h.ReportDate)
            .OrderByDescending(d => d)
            .FirstOrDefaultAsync(cancellationToken);
        if (previous == default)
        {
            // First quarter on record: there is nothing to carry forward, so the
            // combined view degenerates to the plain snapshot — leave the lane empty
            // and let consumers fall back.
            return;
        }

        // Only the two quarters feeding the comparison affect the combined rows; a
        // historical quarter's rebuild (backfill, old amendment) must not re-run the
        // expensive lane.
        if (rebuiltReportDate != latest && rebuiltReportDate != previous)
        {
            return;
        }

        // The repository owns the combined query shapes (and raises this scope's
        // command timeout when composing them); constructed directly over the scoped
        // context — not resolved from DI, so test harnesses that stub the scope with
        // only a DbContext keep working — it reuses the exact semantics the live lane
        // serves, so materialised and live figures can never drift apart.
        var repository = new InstitutionalHoldingRepository(dbContext);
        var activity = await repository
            .GetQuarterlyActivityCombined(latest, previous)
            .ToListAsync(cancellationToken);
        var churn = (
            await repository
                .GetQuarterlyNewSoldOutPositionsCombined(latest, previous)
                .ToListAsync(cancellationToken)
        ).ToDictionary(c => c.CommonStockId);

        var computedAt = DateTime.UtcNow;
        var rows = activity
            .Select(a =>
            {
                churn.TryGetValue(a.CommonStockId, out var c);
                return new StockQuarterlyActivityCombined
                {
                    CommonStockId = a.CommonStockId,
                    ReportDate = latest,
                    PreviousReportDate = previous,
                    CurrentShares = a.CurrentShares,
                    PreviousShares = a.PreviousShares,
                    CurrentValue = a.CurrentValue,
                    PreviousValue = a.PreviousValue,
                    CurrentFilerCount = a.CurrentFilerCount,
                    PreviousFilerCount = a.PreviousFilerCount,
                    NewFilerCount = c?.NewFilerCount ?? 0,
                    SoldOutFilerCount = c?.SoldOutFilerCount ?? 0,
                    ComputedAt = computedAt,
                };
            })
            .ToList();

        if (rows.Count > 0)
        {
            await dbContext
                .Set<StockQuarterlyActivityCombined>()
                .UpsertRange(rows)
                .On(a => new { a.CommonStockId, a.ReportDate })
                .WhenMatched(
                    (_, incoming) =>
                        new StockQuarterlyActivityCombined
                        {
                            PreviousReportDate = incoming.PreviousReportDate,
                            CurrentShares = incoming.CurrentShares,
                            PreviousShares = incoming.PreviousShares,
                            CurrentValue = incoming.CurrentValue,
                            PreviousValue = incoming.PreviousValue,
                            CurrentFilerCount = incoming.CurrentFilerCount,
                            PreviousFilerCount = incoming.PreviousFilerCount,
                            NewFilerCount = incoming.NewFilerCount,
                            SoldOutFilerCount = incoming.SoldOutFilerCount,
                            ComputedAt = incoming.ComputedAt,
                        }
                )
                .RunAsync(cancellationToken);
        }

        // Rows for stocks no longer in the combined view, and any stale rows from a
        // PREVIOUS window's quarter (a new quarter opened before the old lane was
        // retired): one delete covers both.
        var currentStockIds = rows.Select(r => r.CommonStockId).ToList();
        await dbContext
            .Set<StockQuarterlyActivityCombined>()
            .Where(s => s.ReportDate != latest || !currentStockIds.Contains(s.CommonStockId))
            .ExecuteDeleteAsync(cancellationToken);
    }

    // Per-(holder, quarter) AUM aggregates for the institutions browse ranking
    // and per-holder snapshot lookups. Form 13F rows only — Schedule 13D/G rows
    // share the holdings table but carry event dates and single positions, and
    // would otherwise corrupt the per-holder totals. The same quarter-bounded
    // shape as the other steps: the ReportDate-leading covering index narrows
    // the scan to one quarter's slice before the per-holder GROUP BY runs.
    private static async Task UpsertHolderSnapshots(
        EquiblesFinancialDbContext dbContext,
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        var aggregates = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h => h.ReportDate == reportDate && h.FilingType == FilingType.Form13F)
            .GroupBy(h => h.InstitutionalHolderId)
            .Select(g => new
            {
                InstitutionalHolderId = g.Key,
                FilingDate = g.Max(h => h.FilingDate),
                Aum = g.Sum(h => h.Value),
                PositionCount = g.Count(),
                StockCount = g.Select(h => h.CommonStockId).Distinct().Count(),
            })
            .ToListAsync(cancellationToken);

        var computedAt = DateTime.UtcNow;
        var snapshots = aggregates
            .Select(a => new HolderQuarterlySnapshot
            {
                InstitutionalHolderId = a.InstitutionalHolderId,
                ReportDate = reportDate,
                FilingDate = a.FilingDate,
                Aum = a.Aum,
                PositionCount = a.PositionCount,
                StockCount = a.StockCount,
                ComputedAt = computedAt,
            })
            .ToList();

        if (snapshots.Count > 0)
        {
            // Single INSERT … ON CONFLICT (InstitutionalHolderId, ReportDate)
            // DO UPDATE … for the whole batch. Race-free against a parallel
            // rebuild of the same quarter.
            await dbContext
                .Set<HolderQuarterlySnapshot>()
                .UpsertRange(snapshots)
                .On(s => new { s.InstitutionalHolderId, s.ReportDate })
                .WhenMatched(
                    (_, incoming) =>
                        new HolderQuarterlySnapshot
                        {
                            FilingDate = incoming.FilingDate,
                            Aum = incoming.Aum,
                            PositionCount = incoming.PositionCount,
                            StockCount = incoming.StockCount,
                            ComputedAt = incoming.ComputedAt,
                        }
                )
                .RunAsync(cancellationToken);
        }

        // Holders with no remaining 13F rows for this quarter (amendment wiped
        // the filing, or the rows were 13D/G all along) — drop their stale rows
        // so the ranking can't surface a phantom filer. When `snapshots` is
        // empty this collapses to "delete every holder row for this quarter".
        var currentHolderIds = snapshots.Select(s => s.InstitutionalHolderId).ToList();
        await dbContext
            .Set<HolderQuarterlySnapshot>()
            .Where(s =>
                s.ReportDate == reportDate && !currentHolderIds.Contains(s.InstitutionalHolderId)
            )
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task UpsertAumSnapshot(
        EquiblesFinancialDbContext dbContext,
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        // Filtering by ReportDate first narrows the scan via the existing
        // [Index(ReportDate)] btree, so the multi-distinct aggregate only
        // touches one quarter's slice. GroupBy on the same column then folds
        // to a single output row. Form 13F only: a 13G/A whose event date lands
        // exactly on a 13F quarter end (the common annual-amendment case)
        // stores a Schedule row alongside the same holder+stock's 13F row, and
        // without the filter that overlapping stake double counts into the
        // market-wide totals.
        var aggregate = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h => h.ReportDate == reportDate && h.FilingType == FilingType.Form13F)
            .GroupBy(h => h.ReportDate)
            .Select(g => new
            {
                TotalValue = g.Sum(h => h.Value),
                FilerCount = g.Select(h => h.InstitutionalHolderId).Distinct().Count(),
                PositionCount = g.Count(),
                StockCount = g.Select(h => h.CommonStockId).Distinct().Count(),
                FilingCount = g.Select(h => h.AccessionNumber).Distinct().Count(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (aggregate is null)
        {
            // Quarter exists only in the snapshot table now (e.g. all holdings
            // for it were deleted). Drop the stale row so /stats and /trends
            // don't keep reporting phantom AUM for a now-empty quarter.
            await dbContext
                .Set<AumQuarterlySnapshot>()
                .Where(s => s.ReportDate == reportDate)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var snapshot = new AumQuarterlySnapshot
        {
            ReportDate = reportDate,
            TotalValue = aggregate.TotalValue,
            FilerCount = aggregate.FilerCount,
            PositionCount = aggregate.PositionCount,
            StockCount = aggregate.StockCount,
            FilingCount = aggregate.FilingCount,
            ComputedAt = DateTime.UtcNow,
        };

        // Single INSERT … ON CONFLICT (ReportDate) DO UPDATE …
        // Race-free against a parallel rebuild for the same quarter.
        await dbContext
            .Set<AumQuarterlySnapshot>()
            .UpsertRange(snapshot)
            .On(s => s.ReportDate)
            .WhenMatched(
                (_, incoming) =>
                    new AumQuarterlySnapshot
                    {
                        TotalValue = incoming.TotalValue,
                        FilerCount = incoming.FilerCount,
                        PositionCount = incoming.PositionCount,
                        StockCount = incoming.StockCount,
                        FilingCount = incoming.FilingCount,
                        ComputedAt = incoming.ComputedAt,
                    }
            )
            .RunAsync(cancellationToken);
    }

    private static async Task UpsertSectorSnapshots(
        EquiblesFinancialDbContext dbContext,
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        // Same quarter-bounded shape: filter holdings first, then join the
        // stock/industry/sector taxonomy. The aggregate produces one row per
        // (ReportDate, SectorId). Form 13F only — see UpsertAumSnapshot for the
        // quarter-end 13G/A double-count this filter prevents.
        var rows = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h => h.ReportDate == reportDate && h.FilingType == FilingType.Form13F)
            .Join(
                dbContext.Set<CommonStock>(),
                h => h.CommonStockId,
                s => s.Id,
                (h, s) => new { h.Value, s.IndustryId }
            )
            .Join(
                dbContext.Set<Industry>(),
                x => x.IndustryId,
                i => i.Id,
                (x, i) => new { x.Value, i.SectorId }
            )
            .Where(x => x.SectorId != null)
            .GroupBy(x => x.SectorId!.Value)
            .Select(g => new { SectorId = g.Key, TotalValue = g.Sum(x => x.Value) })
            .Join(
                dbContext.Set<Sector>(),
                a => a.SectorId,
                s => s.Id,
                (a, s) =>
                    new
                    {
                        a.SectorId,
                        SectorName = s.Name,
                        a.TotalValue,
                    }
            )
            .ToListAsync(cancellationToken);

        var computedAt = DateTime.UtcNow;
        var snapshots = rows.Select(r => new SectorQuarterlySnapshot
            {
                ReportDate = reportDate,
                SectorId = r.SectorId,
                SectorName = r.SectorName,
                TotalValue = r.TotalValue,
                ComputedAt = computedAt,
            })
            .ToList();

        if (snapshots.Count > 0)
        {
            // Single INSERT … ON CONFLICT (ReportDate, SectorId) DO UPDATE …
            // for the whole batch. Race-free against a parallel rebuild.
            await dbContext
                .Set<SectorQuarterlySnapshot>()
                .UpsertRange(snapshots)
                .On(s => new { s.ReportDate, s.SectorId })
                .WhenMatched(
                    (_, incoming) =>
                        new SectorQuarterlySnapshot
                        {
                            SectorName = incoming.SectorName,
                            TotalValue = incoming.TotalValue,
                            ComputedAt = incoming.ComputedAt,
                        }
                )
                .RunAsync(cancellationToken);
        }

        // Sectors that no longer have positions for this quarter — drop them
        // so /trends doesn't render a chart line for an empty allocation. A
        // single DELETE … WHERE … NOT IN (…) removes the race window that the
        // previous load-then-delete had. When `snapshots` is empty (the
        // quarter has no remaining sector exposure), this collapses to
        // "delete every sector row for this quarter".
        var currentSectorIds = snapshots.Select(s => s.SectorId).ToList();
        await dbContext
            .Set<SectorQuarterlySnapshot>()
            .Where(s => s.ReportDate == reportDate && !currentSectorIds.Contains(s.SectorId))
            .ExecuteDeleteAsync(cancellationToken);
    }

    // Per-stock cross-sectional activity for the conviction heat map: current vs
    // prior-quarter shares/value/filer counts plus the new/sold-out filer counts.
    // Mirrors InstitutionalHoldingRepository.GetQuarterlyActivity and
    // GetQuarterlyNewSoldOutPositions exactly — the same numbers the heat map used
    // to derive live (a two-quarter scan + ~millions of correlated NOT-EXISTS
    // probes), here computed once per dirty quarter and read back per row.
    private static async Task UpsertStockActivitySnapshots(
        EquiblesFinancialDbContext dbContext,
        DateOnly reportDate,
        CancellationToken cancellationToken
    )
    {
        // The prior quarter on record. default(DateOnly) when this is the
        // earliest — no holding row carries that date, so every "previous"
        // predicate below is simply false and all current filers count as new.
        // Form 13F only: Schedule 13D/G rows carry event-driven report dates
        // clustered around quarter-ends, and would otherwise resolve the prior
        // quarter to a sparse non-quarter date where almost no stock has a
        // holding, collapsing every PreviousFilerCount to zero.
        var previousReportDate = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h => h.ReportDate < reportDate && h.FilingType == FilingType.Form13F)
            .Select(h => h.ReportDate)
            .OrderByDescending(d => d)
            .FirstOrDefaultAsync(cancellationToken);
        var hasPrevious = previousReportDate != default;

        var activity = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h =>
                (h.ReportDate == reportDate || h.ReportDate == previousReportDate)
                && h.FilingType == FilingType.Form13F
            )
            .GroupBy(h => h.CommonStockId)
            .Select(g => new
            {
                CommonStockId = g.Key,
                CurrentShares = g.Sum(h => h.ReportDate == reportDate ? h.Shares : 0L),
                PreviousShares = g.Sum(h => h.ReportDate == previousReportDate ? h.Shares : 0L),
                CurrentValue = g.Sum(h => h.ReportDate == reportDate ? h.Value : 0L),
                PreviousValue = g.Sum(h => h.ReportDate == previousReportDate ? h.Value : 0L),
                CurrentFilerCount = g.Where(h => h.ReportDate == reportDate)
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .Count(),
                PreviousFilerCount = g.Where(h => h.ReportDate == previousReportDate)
                    .Select(h => h.InstitutionalHolderId)
                    .Distinct()
                    .Count(),
            })
            .ToListAsync(cancellationToken);

        var churn = (
            await BuildChurnQuery(dbContext, reportDate, previousReportDate)
                .ToListAsync(cancellationToken)
        ).ToDictionary(c => c.CommonStockId);

        var computedAt = DateTime.UtcNow;
        var rows = activity
            .Select(a =>
            {
                churn.TryGetValue(a.CommonStockId, out var c);
                return new StockQuarterlyActivity
                {
                    CommonStockId = a.CommonStockId,
                    ReportDate = reportDate,
                    PreviousReportDate = hasPrevious ? previousReportDate : null,
                    CurrentShares = a.CurrentShares,
                    PreviousShares = a.PreviousShares,
                    CurrentValue = a.CurrentValue,
                    PreviousValue = a.PreviousValue,
                    CurrentFilerCount = a.CurrentFilerCount,
                    PreviousFilerCount = a.PreviousFilerCount,
                    NewFilerCount = c?.NewFilerCount ?? 0,
                    SoldOutFilerCount = c?.SoldOutFilerCount ?? 0,
                    ComputedAt = computedAt,
                };
            })
            .ToList();

        if (rows.Count > 0)
        {
            await dbContext
                .Set<StockQuarterlyActivity>()
                .UpsertRange(rows)
                .On(a => new { a.CommonStockId, a.ReportDate })
                .WhenMatched(
                    (_, incoming) =>
                        new StockQuarterlyActivity
                        {
                            PreviousReportDate = incoming.PreviousReportDate,
                            CurrentShares = incoming.CurrentShares,
                            PreviousShares = incoming.PreviousShares,
                            CurrentValue = incoming.CurrentValue,
                            PreviousValue = incoming.PreviousValue,
                            CurrentFilerCount = incoming.CurrentFilerCount,
                            PreviousFilerCount = incoming.PreviousFilerCount,
                            NewFilerCount = incoming.NewFilerCount,
                            SoldOutFilerCount = incoming.SoldOutFilerCount,
                            ComputedAt = incoming.ComputedAt,
                        }
                )
                .RunAsync(cancellationToken);
        }

        // Stocks with no exposure in either quarter — drop their stale rows for
        // this quarter so the heat map can't render a phantom point.
        var currentStockIds = rows.Select(r => r.CommonStockId).ToList();
        await dbContext
            .Set<StockQuarterlyActivity>()
            .Where(s => s.ReportDate == reportDate && !currentStockIds.Contains(s.CommonStockId))
            .ExecuteDeleteAsync(cancellationToken);
    }

    // Per-stock churn: the same numbers GetQuarterlyNewSoldOutPositions defines ("new" =
    // holder holds the stock this quarter but not last; "sold-out" = the reverse), computed
    // as two grouped passes instead of that method's correlated NOT-EXISTS probes. The inner
    // grouping collapses the two-quarter slice to one row per (stock, holder) with presence
    // flags; the outer grouping counts the flag combinations per stock. The probe-based shape
    // re-seeked the index once per scanned row — millions of lookups, ~35s per rebuild at
    // prod scale — while this shape is one index-only scan plus two hash aggregates (~8x
    // faster), and a rebuild runs on every dirty-quarter drain. Internal so the translation
    // test can pin that the chained-GroupBy shape stays translatable on the Npgsql provider.
    internal static IQueryable<MarketWideStockChurn> BuildChurnQuery(
        EquiblesFinancialDbContext dbContext,
        DateOnly reportDate,
        DateOnly previousReportDate
    ) =>
        dbContext
            .Set<InstitutionalHolding>()
            .Where(h =>
                (h.ReportDate == reportDate || h.ReportDate == previousReportDate)
                && h.FilingType == FilingType.Form13F
            )
            .GroupBy(h => new { h.CommonStockId, h.InstitutionalHolderId })
            .Select(g => new
            {
                g.Key.CommonStockId,
                HasCurrent = g.Max(h => h.ReportDate == reportDate ? 1 : 0),
                HasPrevious = g.Max(h => h.ReportDate == previousReportDate ? 1 : 0),
            })
            .GroupBy(p => p.CommonStockId)
            .Select(g => new MarketWideStockChurn
            {
                CommonStockId = g.Key,
                NewFilerCount = g.Count(p => p.HasCurrent == 1 && p.HasPrevious == 0),
                SoldOutFilerCount = g.Count(p => p.HasPrevious == 1 && p.HasCurrent == 0),
            });
}
