using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.BusinessLogic.Models;

namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>
/// The listing a unified holder list reads and what every lane shares: the 13F quarter it presents,
/// the issuer's effective splits and today's date.
/// </summary>
public sealed class StockOwnershipScope
{
    public EquityIssuer Stock { get; init; }

    /// <summary>The exact listed ticker the caller named, in the stored dash form.</summary>
    public string ListedTicker { get; init; }

    /// <summary>
    /// True for an ETF series or a secondary security, read on its exact listing; a primary
    /// operating company reads every class of the issuer.
    /// </summary>
    public bool ExactListing { get; init; }

    /// <summary>The newest 13F quarter and how it is presented; null when the listing has no 13F rows.</summary>
    public StockQuarterAnchor Anchor { get; init; }

    /// <summary>Every effective split of the issuer, scoped per listed series on use.</summary>
    public IReadOnlyList<StockSplit> Splits { get; init; } = [];

    public DateOnly Today { get; init; }

    public string PrimaryTicker => Stock.Presentation.Listing.Ticker;

    public bool IsPrimaryListing =>
        string.Equals(ListedTicker, PrimaryTicker, StringComparison.OrdinalIgnoreCase);

    /// <summary>The splits that changed a count observed on one listed series; null means the primary.</summary>
    public List<StockSplit> SplitsFor(string listedTicker)
    {
        var ticker = listedTicker ?? PrimaryTicker;
        if (!_splitsByTicker.TryGetValue(ticker, out var scoped))
        {
            scoped = ShareCountSplitScope.ForListedSeries(Splits, PrimaryTicker, ticker);
            _splitsByTicker[ticker] = scoped;
        }
        return scoped;
    }

    private readonly Dictionary<string, List<StockSplit>> _splitsByTicker = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>Restates a count observed on <paramref name="asOf"/> onto today's split basis.</summary>
    public long Restate(long shares, DateOnly asOf, string listedTicker) =>
        SplitAdjustment.AdjustShareCount(shares, asOf, SplitsFor(listedTicker));

    /// <summary>
    /// True when a split after <paramref name="asOf"/> cannot be attributed on the listed series, so
    /// the restated count is only the filed one.
    /// </summary>
    public bool SplitUnresolved(DateOnly asOf, string listedTicker) =>
        ListingId(listedTicker) is { } listingId
        && ShareCountSplitScope.HasUnresolvedBasis(Splits, listingId, asOf);

    private Guid? ListingId(string listedTicker)
    {
        var ticker = listedTicker ?? PrimaryTicker;
        if (string.Equals(ticker, PrimaryTicker, StringComparison.OrdinalIgnoreCase))
            return Stock.Presentation.EquityListingId;
        return Stock
            .Securities.SelectMany(security => security.Listings)
            .Where(listing =>
                listing.MarketCountryCode == "US"
                && string.Equals(listing.Ticker, ticker, StringComparison.OrdinalIgnoreCase)
            )
            .Select(listing => (Guid?)listing.Id)
            .FirstOrDefault();
    }
}
