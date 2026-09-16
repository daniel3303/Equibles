using Equibles.EquityMarkets.Data.Catalog;
using Equibles.Integrations.DelayedTrades;

namespace Equibles.DelayedTrades.BusinessLogic.Prints;

// The two print classes every figure rests on, decided from the venue's own MMT flags and nothing inferred.
public static class DelayedTradePrintFilter
{
    public const string MonetaryNotation = "MONE";
    public const string DarkOrderBookMechanism = "3";

    // Central limit order book and periodic auction; the closing auction publishes as mechanism 1.
    public static readonly IReadOnlySet<string> PriceFormingMechanisms = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "1",
        "5",
    };

    // A counted print is a real trade with a price and a size in money terms; the modification ledger runs first.
    public static bool IsCounted(DelayedTradePrint print) =>
        print != null
        && !print.MissingPrice
        && print.Price > 0
        && print.Quantity > 0
        && print.PriceNotation == MonetaryNotation;

    // A price-forming print is a counted lit print with no benchmark, negotiation or non-contributing flag.
    public static bool IsPriceForming(DelayedTradePrint print) =>
        IsCounted(print)
        && PriceFormingMechanisms.Contains(print.MarketMechanism)
        && IsUnflagged(print.BenchmarkIndicator)
        && IsUnflagged(print.NegotiationIndicator)
        && (IsUnflagged(print.ContributionToPrice) || print.ContributionToPrice == "P");

    public static bool IsDark(DelayedTradePrint print) =>
        print.MarketMechanism == DarkOrderBookMechanism;

    // The file is the delayed medium, but the lane still refuses a print younger than the delay it promises.
    public static bool IsDelayed(DelayedTradePrint print, DateTime fetchedAtUtc, TimeSpan delay) =>
        print.PublishedAtUtc <= fetchedAtUtc - delay;

    // The venue's currency token must resolve to the listing's stored unit; a pence file matches a GBP/0.01 listing.
    public static bool MatchesQuotation(
        DelayedTradePrint print,
        string tradingCurrency,
        decimal? quoteUnitMultiplier
    ) => EquityQuotationUnits.Matches(print.Currency, tradingCurrency, quoteUnitMultiplier);

    private static bool IsUnflagged(string token) => string.IsNullOrEmpty(token) || token == "-";
}
