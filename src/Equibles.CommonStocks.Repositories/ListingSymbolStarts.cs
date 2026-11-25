using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;

namespace Equibles.CommonStocks.Repositories;

/// <summary>
/// The recorded <see cref="EquityListingSymbolStart"/> boundaries, applied to a symbol map built
/// from today's tickers: on a date before a listing's start, its symbol named another security.
/// </summary>
public sealed class ListingSymbolStarts
{
    public static readonly ListingSymbolStarts None = new([]);

    private readonly Dictionary<Guid, (string Ticker, DateOnly StartsOn)[]> _starts;

    public ListingSymbolStarts(IEnumerable<EquityListingSymbolStart> starts)
    {
        _starts = starts
            .Where(start => TickerNormalizer.NormalizeIdentity(start.Ticker) != null)
            .GroupBy(start => start.EquityListingId)
            .ToDictionary(
                group => group.Key,
                group =>
                    group
                        .Select(start =>
                            (TickerNormalizer.NormalizeIdentity(start.Ticker), start.StartsOn)
                        )
                        .ToArray()
            );
    }

    public bool IsEmpty => _starts.Count == 0;

    public bool Admits(EquityListingReference listing, DateOnly date)
    {
        if (!_starts.TryGetValue(listing.EquityListingId, out var starts))
            return true;
        var ticker = TickerNormalizer.NormalizeIdentity(listing.ListedTicker);
        return starts.All(start => start.Ticker != ticker || date >= start.StartsOn);
    }

    /// <summary>
    /// The map without the listings whose symbol named another security on <paramref name="date"/>;
    /// the same instance when none does.
    /// </summary>
    public Dictionary<string, EquityListingReference> Admitted(
        Dictionary<string, EquityListingReference> map,
        DateOnly date
    )
    {
        if (IsEmpty || map.Values.All(listing => Admits(listing, date)))
            return map;
        return map.Where(entry => Admits(entry.Value, date))
            .ToDictionary(entry => entry.Key, entry => entry.Value, map.Comparer);
    }
}
