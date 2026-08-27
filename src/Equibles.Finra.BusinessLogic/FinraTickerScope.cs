using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;

namespace Equibles.Finra.BusinessLogic;

/// <summary>
/// FINRA rows currently carry issuer identity, not exact listed-symbol identity. A secondary
/// listing must therefore be refused instead of being relabelled with the issuer's primary data.
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
}
