using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;

namespace Equibles.Finra.BusinessLogic;

/// <summary>
/// Guards models whose inputs still carry issuer identity even though raw FINRA rows now retain
/// exact listed-symbol identity.
/// </summary>
public static class FinraTickerScope
{
    public static string SecondaryListingUnavailable(
        CommonStock stock,
        string requestedTicker,
        string dataset
    )
    {
        if (!SecondaryTickerPolicy.IsSecondarySymbol(stock, requestedTicker))
            return null;

        var listedTicker = SecondaryTickerPolicy.ResolveListedTicker(stock, requestedTicker);
        return $"No exact {dataset} series is available for {listedTicker}. It is a separate "
            + $"listing on the same SEC filer as {stock.Ticker} ({stock.Name}); {stock.Ticker}'s "
            + "FINRA rows are not substituted.";
    }

    public static string IssuerDerivedModelUnavailable(
        CommonStock stock,
        string requestedTicker,
        string dataset
    )
    {
        var listedTicker = SecondaryTickerPolicy.ResolveListedTicker(stock, requestedTicker);
        if (!SecondaryTickerPolicy.RequiresExactListingScope(stock, listedTicker))
            return null;

        return $"No {dataset} model is available for {listedTicker}. Its raw FINRA series is "
            + "listing-specific, but this model still depends on issuer-level shares outstanding "
            + "and primary-listing market factors.";
    }
}
