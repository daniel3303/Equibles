using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.Holdings.Repositories.Extensions;

public static class HoldingActivityLegQueries
{
    // Per-stock 13F totals count common shares only. A put or call row states the shares under
    // the option at their full value, and a principal row states a debt amount, so neither is
    // shares held or equity value: one filer's option rows once read as more than a company's
    // whole share count. The row itself stays, with zero shares and value, so its filer still
    // counts as a filer of the stock and its listing still gets a row. Every activity snapshot
    // and each of its live fallbacks sums through this one projection, so they cannot drift.
    public static IQueryable<HoldingActivityLeg> AsActivityLegs(
        this IQueryable<InstitutionalHolding> holdings
    ) =>
        holdings.Select(holding => new HoldingActivityLeg
        {
            EquityIssuerId = holding.EquityIssuerId,
            InstitutionalHolderId = holding.InstitutionalHolderId,
            ReportDate = holding.ReportDate,
            ListedTicker = holding.ListedTicker,
            Shares =
                holding.ShareType == ShareType.Shares && holding.OptionType == null
                    ? holding.Shares
                    : 0L,
            Value =
                holding.ShareType == ShareType.Shares && holding.OptionType == null
                    ? holding.Value
                    : 0L,
        });

    // The same rule for rows already loaded: a per-holder or per-stock total summed in memory
    // beside a snapshot figure (a holders table over its trend) has to count the legs the
    // snapshot counts.
    public static bool IsCommonShareLeg(this InstitutionalHolding holding) =>
        holding.ShareType == ShareType.Shares && holding.OptionType == null;
}
