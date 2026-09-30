using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories.Models;

namespace Equibles.CommonStocks.Repositories.Extensions;

public static class EquityListingSourceQueryExtensions
{
    public static IQueryable<EquityListing> WithDirectoryInstrumentIdentity(
        this IQueryable<EquityListing> listings
    ) =>
        listings.Where(listing =>
            listing.Security.Isin != null
            || listing.SourceIdentifiers.Any(identifier =>
                identifier.IsDirectoryListed
                && listing.Security.SourceIdentifiers.Any(security =>
                    security.Source == identifier.Source
                )
            )
        );

    // Reuse this predicate inside each writer's directory-lock transaction. A previous
    // successful check cannot authorize a later write after that lock was released.
    public static IQueryable<EquityListing> ForVerifiedSource(
        this IQueryable<EquityListing> listings,
        EquityListingSourceBinding binding
    ) =>
        listings
            .WithDirectoryInstrumentIdentity()
            .Where(listing =>
                listing.Id == binding.EquityListingId
                && listing.Security.EquityIssuerId == binding.EquityIssuerId
                && listing.Active
                && listing.IdentityState == EquityIdentityState.Verified
                && listing.Ticker == binding.Ticker
                && listing.MarketIdentifierCode == binding.MarketIdentifierCode
                && listing.MarketCountryCode == binding.MarketCountryCode
                && listing.Security.Isin == binding.Isin
                && (
                    binding.EquitySecurityId == null
                    || listing.EquitySecurityId == binding.EquitySecurityId
                )
                && (
                    binding.Isin != null
                    || binding.EquitySecurityId != null && binding.EquitySecurityId != Guid.Empty
                )
                && listing.TradingCurrency == binding.Currency
                && listing.QuoteUnitMultiplier == binding.QuoteUnitMultiplier
                && binding.SourceMarketIdentifierCodes.Contains(listing.MarketIdentifierCode)
                && !listings.Any(other =>
                    other.Id != listing.Id
                    && other.Active
                    && other.MarketCountryCode == binding.MarketCountryCode
                    && other.Ticker == binding.Ticker
                    && binding.SourceMarketIdentifierCodes.Contains(other.MarketIdentifierCode)
                )
            );
}
