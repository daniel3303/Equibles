using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
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

        var distinctDates = dbContext
            .Set<InstitutionalHolding>()
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
        // to a single output row.
        var aggregate = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h => h.ReportDate == reportDate)
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
        // (ReportDate, SectorId).
        var rows = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h => h.ReportDate == reportDate)
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
}
