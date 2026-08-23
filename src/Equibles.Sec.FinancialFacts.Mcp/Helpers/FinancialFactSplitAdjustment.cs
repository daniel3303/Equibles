using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Mcp.Helpers;

internal static class FinancialFactSplitAdjustment
{
    internal const string Note =
        "Per-share values are split-adjusted to today's share basis using splits effective "
        + "strictly after each fact's Filed date.";

    internal static bool IsPerShare(FinancialFact fact) =>
        fact.Unit?.Contains('/', StringComparison.Ordinal) == true;

    internal static decimal Restate(
        FinancialFact fact,
        IReadOnlyList<StockSplit> splits,
        out bool adjusted
    )
    {
        if (!IsPerShare(fact))
        {
            adjusted = false;
            return fact.Value;
        }

        var factor = SplitAdjustment.ShareCountFactor(fact.FiledDate, splits);
        adjusted = factor != 0m && factor != 1m;
        return SplitAdjustment.AdjustPerShareValue(fact.Value, factor);
    }
}
