using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Mcp.Helpers;

internal static class FinancialFactSplitAdjustment
{
    internal const string Note =
        "Per-share values are split-adjusted to today's share basis using splits effective "
        + "strictly after each fact's Filed date.";

    // Only a share-denominated ratio may be split-adjusted. Filers publish many
    // other ratio units (USD/bbl, USD/MMBTU, USD/EUR, shares/USD, USD/Shareholder, …)
    // whose values a stock split does not change, so the denominator measure must
    // literally be shares; anything else stays as filed.
    internal static bool IsPerShare(FinancialFact fact)
    {
        var unit = fact.Unit;
        if (unit == null)
        {
            return false;
        }

        var separator = unit.IndexOf('/', StringComparison.Ordinal);
        if (separator <= 0 || separator != unit.LastIndexOf('/'))
        {
            return false;
        }

        var denominator = unit[(separator + 1)..].Trim();
        return denominator.Equals("shares", StringComparison.OrdinalIgnoreCase)
            || denominator.Equals("share", StringComparison.OrdinalIgnoreCase);
    }

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
