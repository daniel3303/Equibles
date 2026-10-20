using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Restates fund share balances reported at a portfolio date onto one quarter end's split basis,
/// per listed series: only splits effective by the quarter end are loaded, so a balance moves by
/// the splits between its portfolio date and the quarter end. A balance whose basis cannot be
/// attributed is kept as filed and flagged.
/// </summary>
internal sealed class FundShareRestater
{
    internal sealed record Listing(
        Guid IssuerId,
        Guid ListingId,
        string Ticker,
        bool IsPresentation
    );

    private readonly Dictionary<Guid, List<StockSplit>> _splits;
    private readonly Dictionary<Guid, List<Listing>> _listings;
    private readonly Dictionary<
        (Guid, string),
        (List<StockSplit> Scope, Guid? ListingId)
    > _scopes = [];
    private readonly Dictionary<
        (Guid, string, DateOnly),
        (decimal Factor, bool Unresolved)
    > _factors = [];

    internal FundShareRestater(IEnumerable<StockSplit> splits, IEnumerable<Listing> listings)
    {
        _splits = splits
            .GroupBy(split => split.EquityIssuerId)
            .ToDictionary(group => group.Key, group => group.ToList());
        _listings = listings
            .GroupBy(listing => listing.IssuerId)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    /// <param name="listedTicker">The listed series, or null for the issuer's primary listing.</param>
    internal (long Shares, bool Unresolved) Restate(
        Guid issuerId,
        string listedTicker,
        decimal balance,
        DateOnly asOf
    )
    {
        if (!_splits.TryGetValue(issuerId, out var splits))
            return (Round(balance), false);
        var key = (issuerId, listedTicker ?? "", asOf);
        if (!_factors.TryGetValue(key, out var factor))
        {
            var (scope, listingId) = Scope(issuerId, listedTicker, splits);
            factor =
                listingId is { } id && ShareCountSplitScope.HasUnresolvedBasis(splits, id, asOf)
                    ? (1m, true)
                    : (SplitAdjustment.ShareCountFactor(asOf, scope), false);
            _factors[key] = factor;
        }
        return (Round(balance * factor.Factor), factor.Unresolved);
    }

    private (List<StockSplit> Scope, Guid? ListingId) Scope(
        Guid issuerId,
        string listedTicker,
        List<StockSplit> splits
    )
    {
        var key = (issuerId, listedTicker ?? "");
        if (_scopes.TryGetValue(key, out var scoped))
            return scoped;
        var listings = _listings.GetValueOrDefault(issuerId) ?? [];
        var primary = listings.FirstOrDefault(listing => listing.IsPresentation);
        var listing =
            listedTicker == null
                ? primary
                : listings.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.Ticker,
                        listedTicker,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
        var ticker = listedTicker ?? primary?.Ticker;
        scoped = (
            ticker == null || primary == null
                ? []
                : ShareCountSplitScope.ForListedSeries(splits, primary.Ticker, ticker),
            listing?.ListingId
        );
        _scopes[key] = scoped;
        return scoped;
    }

    private static long Round(decimal shares) =>
        (long)Math.Round(shares, MidpointRounding.AwayFromZero);
}
