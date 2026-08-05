using Equibles.Data;
using Equibles.Holdings.Data.Models;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Re-derives the stored rollups that carry a holding's value after that value changes in place.
/// </summary>
/// <remarks>
/// <para>
/// Two aggregates are written at import and never recomputed on read:
/// <c>InstitutionalFiling.TotalValue</c> (what the AUM surfaces rank on) and the per-quarter
/// <see cref="AumQuarterlySnapshot"/> (rebuilt by the drain worker, but only when its
/// <c>DirtyAt</c> is stamped). Any pass that changes stored <c>Value</c> figures — the repricing
/// lane, the filed-value fallback, the impossible-position repair — must route the affected
/// accessions and quarters through here, or the aggregate keeps advertising the old figure.
/// </para>
/// <para>
/// Idempotent and re-entrant: re-summing an already-correct filing writes nothing, and the dirty
/// stamp is an insert-or-set-when-null upsert (the same statement
/// <c>Filings13FImportedConsumer</c> uses), so concurrent stampers converge.
/// </para>
/// </remarks>
internal static class HoldingsRollupRefresher
{
    internal static async Task Refresh(
        EquiblesFinancialDbContext dbContext,
        IReadOnlyCollection<string> accessionNumbers,
        IReadOnlyCollection<DateOnly> reportDates,
        CancellationToken cancellationToken
    )
    {
        await RealignFilingTotals(dbContext, accessionNumbers, cancellationToken);
        await MarkAumSnapshotsDirty(dbContext, reportDates, cancellationToken);
    }

    /// <summary>Re-sums <c>InstitutionalFiling.TotalValue</c> for the given accessions.</summary>
    internal static async Task<int> RealignFilingTotals(
        EquiblesFinancialDbContext dbContext,
        IReadOnlyCollection<string> accessionNumbers,
        CancellationToken cancellationToken
    )
    {
        var accessions = accessionNumbers.Where(a => a != null).Distinct().ToList();
        if (accessions.Count == 0)
        {
            return 0;
        }

        var totals = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h => accessions.Contains(h.AccessionNumber))
            .GroupBy(h => h.AccessionNumber)
            .Select(g => new { AccessionNumber = g.Key, TotalValue = g.Sum(h => h.Value) })
            .ToListAsync(cancellationToken);

        var totalByAccession = totals.ToDictionary(t => t.AccessionNumber, t => t.TotalValue);

        var filings = await dbContext
            .Set<InstitutionalFiling>()
            .Where(f => accessions.Contains(f.AccessionNumber))
            .ToListAsync(cancellationToken);

        var realigned = 0;
        foreach (var filing in filings)
        {
            if (
                totalByAccession.TryGetValue(filing.AccessionNumber, out var total)
                && filing.TotalValue != total
            )
            {
                filing.TotalValue = total;
                realigned++;
            }
        }

        if (realigned > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return realigned;
    }

    /// <summary>
    /// Stamps the quarters' AUM snapshots dirty so the drain worker rebuilds them.
    /// </summary>
    internal static async Task MarkAumSnapshotsDirty(
        EquiblesFinancialDbContext dbContext,
        IReadOnlyCollection<DateOnly> reportDates,
        CancellationToken cancellationToken
    )
    {
        var quarters = reportDates.Distinct().ToList();
        if (quarters.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var stubs = quarters
            .Select(reportDate => new AumQuarterlySnapshot
            {
                ReportDate = reportDate,
                TotalValue = 0L,
                FilerCount = 0,
                PositionCount = 0,
                StockCount = 0,
                FilingCount = 0,
                ComputedAt = now,
                DirtyAt = now,
            })
            .ToArray();

        // The InMemory test provider gets a plain load-modify-save equivalent — the atomic upsert
        // below is Postgres-specific and its semantics are already pinned on the real backend by
        // the Filings13FImportedConsumer tests.
        if (!dbContext.Database.IsRelational())
        {
            var existingRows = await dbContext
                .Set<AumQuarterlySnapshot>()
                .Where(s => quarters.Contains(s.ReportDate))
                .ToListAsync(cancellationToken);
            var existingByDate = existingRows.ToDictionary(s => s.ReportDate);
            foreach (var stub in stubs)
            {
                if (existingByDate.TryGetValue(stub.ReportDate, out var existing))
                {
                    existing.DirtyAt ??= now;
                }
                else
                {
                    dbContext.Set<AumQuarterlySnapshot>().Add(stub);
                }
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await dbContext
            .Set<AumQuarterlySnapshot>()
            .UpsertRange(stubs)
            .On(s => s.ReportDate)
            .WhenMatched(
                (existing, incoming) =>
                    new AumQuarterlySnapshot { DirtyAt = existing.DirtyAt ?? incoming.DirtyAt }
            )
            .RunAsync(cancellationToken);
    }
}
