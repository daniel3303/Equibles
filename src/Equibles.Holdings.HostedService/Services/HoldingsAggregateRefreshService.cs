using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
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
/// Two entry points:
/// <list type="bullet">
///   <item><c>RebuildQuarterAsync</c> — single quarter, bounded work; called
///     by the event-driven path on each 13F import.</item>
///   <item><c>RebuildAllAsync</c> — enumerates distinct ReportDate values and
///     rebuilds each in its own DbContext scope; called by the daily
///     safety-net job and the one-time first-boot backfill.</item>
/// </list>
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

    public Task RebuildQuarterAsync(DateOnly reportDate, CancellationToken cancellationToken) =>
        RebuildQuarterAsync(reportDate, commandTimeout: null, cancellationToken);

    public async Task RebuildQuarterAsync(
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
        List<DateOnly> reportDates;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
            if (commandTimeout is not null)
            {
                dbContext.Database.SetCommandTimeout(commandTimeout.Value);
            }
            reportDates = await dbContext
                .Set<InstitutionalHolding>()
                .Select(h => h.ReportDate)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Rebuilding holdings aggregate snapshots for {Count} quarter(s)",
            reportDates.Count
        );

        foreach (var reportDate in reportDates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = _scopeFactory.CreateAsyncScope();
            await RebuildQuarterInScope(
                scope.ServiceProvider,
                reportDate,
                commandTimeout,
                cancellationToken
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
        var aumRepo = services.GetRequiredService<AumQuarterlySnapshotRepository>();
        var sectorRepo = services.GetRequiredService<SectorQuarterlySnapshotRepository>();

        await UpsertAumSnapshot(dbContext, aumRepo, reportDate, cancellationToken);
        await UpsertSectorSnapshots(dbContext, sectorRepo, reportDate, cancellationToken);
    }

    private async Task UpsertAumSnapshot(
        EquiblesFinancialDbContext dbContext,
        AumQuarterlySnapshotRepository aumRepo,
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

        var existing = await aumRepo
            .GetAll()
            .FirstOrDefaultAsync(s => s.ReportDate == reportDate, cancellationToken);

        if (aggregate is null)
        {
            // Quarter exists only in the snapshot table now (e.g. all holdings
            // for it were deleted). Drop the stale row so /stats and /trends
            // don't keep reporting phantom AUM for a now-empty quarter.
            if (existing is not null)
            {
                aumRepo.Delete(existing);
                await aumRepo.SaveChanges();
            }
            return;
        }

        if (existing is null)
        {
            aumRepo.Add(
                new AumQuarterlySnapshot
                {
                    ReportDate = reportDate,
                    TotalValue = aggregate.TotalValue,
                    FilerCount = aggregate.FilerCount,
                    PositionCount = aggregate.PositionCount,
                    StockCount = aggregate.StockCount,
                    FilingCount = aggregate.FilingCount,
                    ComputedAt = DateTime.UtcNow,
                }
            );
        }
        else
        {
            existing.TotalValue = aggregate.TotalValue;
            existing.FilerCount = aggregate.FilerCount;
            existing.PositionCount = aggregate.PositionCount;
            existing.StockCount = aggregate.StockCount;
            existing.FilingCount = aggregate.FilingCount;
            existing.ComputedAt = DateTime.UtcNow;
        }

        await aumRepo.SaveChanges();
    }

    private async Task UpsertSectorSnapshots(
        EquiblesFinancialDbContext dbContext,
        SectorQuarterlySnapshotRepository sectorRepo,
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

        var existing = await sectorRepo
            .GetAll()
            .Where(s => s.ReportDate == reportDate)
            .ToListAsync(cancellationToken);

        var existingBySector = existing.ToDictionary(s => s.SectorId);
        var seen = new HashSet<Guid>();

        foreach (var row in rows)
        {
            seen.Add(row.SectorId);
            if (existingBySector.TryGetValue(row.SectorId, out var current))
            {
                current.SectorName = row.SectorName;
                current.TotalValue = row.TotalValue;
                current.ComputedAt = DateTime.UtcNow;
            }
            else
            {
                sectorRepo.Add(
                    new SectorQuarterlySnapshot
                    {
                        ReportDate = reportDate,
                        SectorId = row.SectorId,
                        SectorName = row.SectorName,
                        TotalValue = row.TotalValue,
                        ComputedAt = DateTime.UtcNow,
                    }
                );
            }
        }

        // Sectors that no longer have positions for this quarter — drop them
        // so /trends doesn't render a chart line for an empty allocation.
        foreach (var stale in existing.Where(s => !seen.Contains(s.SectorId)))
        {
            sectorRepo.Delete(stale);
        }

        await sectorRepo.SaveChanges();
    }
}
