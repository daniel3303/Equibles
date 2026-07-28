using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Withdraws the derived value from stored positions that are larger than the issuer they are in.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ImpossiblePositionGuard"/> stops these at import, but a quarterly data set is
/// processed once and never revisited, so every row ingested before the guard existed keeps its
/// wrong figure forever. This pass applies the same rule to what is already stored.
/// </para>
/// <para>
/// It runs every cycle and is self-terminating: once a row is marked it no longer matches, so the
/// second pass over a repaired database does one cheap query and stops. That also makes it the
/// heal path for any row a future import writes before its issuer's size is known.
/// </para>
/// </remarks>
[Service]
public class ImpossiblePositionRepairService
{
    // The database narrows to positions above the multiple; the guard then makes the real decision
    // in memory, so the rule lives in exactly one place and the two paths cannot drift.
    private const int BatchSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImpossiblePositionRepairService> _logger;

    public ImpossiblePositionRepairService(
        IServiceScopeFactory scopeFactory,
        ILogger<ImpossiblePositionRepairService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<int> Repair(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        // Candidates only — the coarse "more shares than the issuer has" filter, which SQL can
        // serve from the existing indexes. Whether the issuer's own figures are trustworthy enough
        // to act on is decided by the guard below, not here.
        var candidates = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h => !h.ValueUnavailable && h.Shares > 0)
            .Join(
                dbContext.Set<CommonStock>(),
                h => h.CommonStockId,
                cs => cs.Id,
                (h, cs) =>
                    new
                    {
                        Holding = h,
                        cs.SharesOutStanding,
                        cs.MarketCapitalization,
                    }
            )
            .Where(x =>
                x.SharesOutStanding > 0
                && x.MarketCapitalization > 0
                && x.Holding.Shares
                    > x.SharesOutStanding * ImpossiblePositionGuard.SharesOutstandingMultiple
            )
            .ToListAsync(cancellationToken);

        var repaired = 0;
        foreach (var candidate in candidates)
        {
            if (
                !ImpossiblePositionGuard.ExceedsTheIssuer(
                    candidate.Holding.Shares,
                    candidate.SharesOutStanding,
                    candidate.MarketCapitalization
                )
            )
            {
                continue;
            }

            candidate.Holding.Value = 0L;
            candidate.Holding.ValuePending = false;
            candidate.Holding.ValueUnavailable = true;
            repaired++;

            if (repaired % BatchSize == 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        if (repaired > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                "Withdrew the derived value from {Repaired} position(s) reporting more shares than "
                    + "the issuer has, out of {Candidates} candidate(s); the rest sit on a share "
                    + "count the issuer's own figures cannot vouch for",
                repaired,
                candidates.Count
            );
        }

        var realigned = await RealignFilingTotals(dbContext, cancellationToken);
        if (realigned > 0)
        {
            _logger.LogWarning(
                "Re-summed {Realigned} filing rollup(s) still carrying a withdrawn position's value",
                realigned
            );
        }

        return repaired;
    }

    /// <summary>
    /// Re-sums the per-accession rollup of any filing holding a position whose value was withdrawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>InstitutionalFiling.TotalValue</c> is a stored total written once at import, so
    /// withdrawing a holding's value leaves the rollup carrying it — and the rollup is what the AUM
    /// surfaces rank on, which is exactly where the wrong figure shows. Marking the positions alone
    /// left the homepage strip still advertising a $100.8B portfolio while every position behind it
    /// read zero.
    /// </para>
    /// <para>
    /// Driven off the marked positions rather than off what this pass just changed, so it also
    /// heals filings whose positions were marked by an earlier run — and re-running it is free once
    /// the totals agree.
    /// </para>
    /// </remarks>
    private static async Task<int> RealignFilingTotals(
        EquiblesFinancialDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var accessionNumbers = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h => h.ValueUnavailable && h.AccessionNumber != null)
            .Select(h => h.AccessionNumber)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (accessionNumbers.Count == 0)
        {
            return 0;
        }

        var totals = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h => accessionNumbers.Contains(h.AccessionNumber))
            .GroupBy(h => h.AccessionNumber)
            .Select(g => new { AccessionNumber = g.Key, TotalValue = g.Sum(h => h.Value) })
            .ToListAsync(cancellationToken);

        var totalByAccession = totals.ToDictionary(t => t.AccessionNumber, t => t.TotalValue);

        var filings = await dbContext
            .Set<InstitutionalFiling>()
            .Where(f => accessionNumbers.Contains(f.AccessionNumber))
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
}
