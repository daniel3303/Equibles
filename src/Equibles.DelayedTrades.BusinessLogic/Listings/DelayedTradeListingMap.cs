using Equibles.DelayedTrades.Repositories;

namespace Equibles.DelayedTrades.BusinessLogic.Listings;

// (ISIN, venue) to one verified listing; a key claimed twice is refused rather than guessed.
public sealed class DelayedTradeListingMap
{
    private readonly Dictionary<(string Isin, string Mic), DelayedTradeListingReference> _byKey =
        new();
    private readonly HashSet<(string Isin, string Mic)> _ambiguous = [];

    public int Count => _byKey.Count;
    public int AmbiguousCount => _ambiguous.Count;

    public static DelayedTradeListingMap Build(IEnumerable<DelayedTradeListingReference> listings)
    {
        var map = new DelayedTradeListingMap();
        foreach (var listing in listings)
        {
            if (
                string.IsNullOrEmpty(listing.Isin)
                || string.IsNullOrEmpty(listing.MarketIdentifierCode)
                || listing.QuoteUnitMultiplier == null
                || listing.TradingCurrency == null
            )
                continue;
            var key = (listing.Isin, listing.MarketIdentifierCode);
            if (map._ambiguous.Contains(key))
                continue;
            if (map._byKey.Remove(key))
            {
                map._ambiguous.Add(key);
                continue;
            }
            map._byKey[key] = listing;
        }
        return map;
    }

    public bool TryResolve(string isin, string mic, out DelayedTradeListingReference listing) =>
        _byKey.TryGetValue((isin, mic), out listing);

    public bool IsAmbiguous(string isin, string mic) => _ambiguous.Contains((isin, mic));
}
