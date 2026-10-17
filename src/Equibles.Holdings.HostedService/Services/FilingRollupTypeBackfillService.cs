using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Restamps <see cref="InstitutionalFiling.FilingType"/> on rollup rows written before the
/// column existed, from the holdings rows the rollup summarises.
/// </summary>
/// <remarks>
/// The column's migration defaults every existing row to <see cref="FilingType.Form13F"/>, which
/// mislabels the Schedule 13D/G rollup rows — and those are exactly the rows whose event dates
/// would otherwise pass for a filer's freshest "13F quarter" in recency-ranked resolution. The
/// sync stamps new rows correctly, so this sweep only ever touches the pre-column backlog:
/// bounded per cycle, self-terminating (a restamped row no longer matches), and a no-op scan
/// once drained. Values are copied from the holdings' own <c>FilingType</c> — never inferred
/// from date shape, because 13D/G rows dated on quarter ends exist. A row typed 13F on a date that
/// is not a calendar quarter end and with no holdings left is a 13D/G rollup whose holdings are
/// gone, so it is removed instead.
/// </remarks>
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

        return await Restamp(dbContext, cancellationToken)
            + await RemoveOffQuarterOrphans(dbContext, cancellationToken);
    }

    private async Task<int> Restamp(
        EquiblesFinancialDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var rows = await dbContext
            .Set<InstitutionalFiling>()
            .Where(f =>
                f.FilingType == FilingType.Form13F
                && dbContext
                    .Set<InstitutionalHolding>()
                    .Any(h =>
                        h.AccessionNumber == f.AccessionNumber && h.FilingType != FilingType.Form13F
                    )
            )
            .OrderBy(f => f.Id)
            .Take(MaxRowsPerCycle)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return 0;
        }

        var accessions = rows.Select(r => r.AccessionNumber).ToList();
        var typeByAccession = (
            await dbContext
                .Set<InstitutionalHolding>()
                .Where(h =>
                    accessions.Contains(h.AccessionNumber) && h.FilingType != FilingType.Form13F
                )
                .Select(h => new { h.AccessionNumber, h.FilingType })
                .Distinct()
                .ToListAsync(cancellationToken)
        )
            .GroupBy(t => t.AccessionNumber, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().FilingType, StringComparer.Ordinal);

        var restamped = 0;
        foreach (var filing in rows)
        {
            if (typeByAccession.TryGetValue(filing.AccessionNumber, out var filingType))
            {
                filing.FilingType = filingType;
                restamped++;
            }
        }

        if (restamped > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Restamped FilingType on {Count} pre-column filing rollup row(s)",
                restamped
            );
        }

        return restamped;
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
