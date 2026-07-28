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

        return repaired;
    }
}
