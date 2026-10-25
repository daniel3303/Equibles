using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Removes 13F-typed filing rollups dated off a calendar quarter end with no holdings left: 13D/G
/// rollups the <see cref="InstitutionalFiling.FilingType"/> migration mislabelled, whose holdings
/// are gone. Every writer stamps the type itself, so this only drains that pre-column backlog.
/// </summary>
[Service]
public class FilingRollupTypeBackfillService
{
    internal const int MaxRowsPerCycle = 50_000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FilingRollupTypeBackfillService> _logger;

    public FilingRollupTypeBackfillService(
        IServiceScopeFactory scopeFactory,
        ILogger<FilingRollupTypeBackfillService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<int> Backfill(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        if (dbContext.Database.IsRelational())
        {
            dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        }

        return await RemoveOffQuarterOrphans(dbContext, cancellationToken);
    }

    // Form 13F reports as of a calendar quarter end and the importers refuse any other period
    // (HoldingsCorpusCoverage.IsCalendarQuarterEnd), so a 13F-typed row on another date with
    // nothing under its accession summarises no 13F.
    private async Task<int> RemoveOffQuarterOrphans(
        EquiblesFinancialDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var orphans = dbContext
            .Set<InstitutionalFiling>()
            .Where(f =>
                f.FilingType == FilingType.Form13F
                && (f.ReportDate.Month % 3 != 0 || f.ReportDate.AddDays(1).Day != 1)
                && !dbContext
                    .Set<InstitutionalHolding>()
                    .Any(h => h.AccessionNumber == f.AccessionNumber)
            );
        var selected = await orphans
            .OrderBy(f => f.Id)
            .Take(MaxRowsPerCycle)
            .Select(f => new { f.Id, f.AccessionNumber })
            .ToListAsync(cancellationToken);
        if (selected.Count == 0)
        {
            return 0;
        }

        // The delete re-checks the condition, so a rollup an import refilled meanwhile stays.
        var ids = selected.Select(f => f.Id).ToList();
        var removed = await orphans
            .Where(f => ids.Contains(f.Id))
            .ExecuteDeleteAsync(cancellationToken);
        _logger.LogInformation(
            "Removed {Removed} of {Selected} selected off-quarter 13F filing rollup row(s) with no holdings; selected: {Accessions}",
            removed,
            selected.Count,
            string.Join(", ", selected.Take(20).Select(f => f.AccessionNumber))
        );
        return removed;
    }
}
