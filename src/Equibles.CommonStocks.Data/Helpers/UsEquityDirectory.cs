using Equibles.CommonStocks.Data.Models;

namespace Equibles.CommonStocks.Data.Helpers;

// SEC and U.S. reference directories describe U.S. listings, including foreign issuers.
// Callers load the issuer's security/listing graph and serialize changes under its write lock.
public static class UsEquityDirectory
{
    public static EquityListing SelectPrimary(EquityIssuer issuer, string ticker)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        var listing = GetOrAddListing(issuer, ticker);
        issuer.Presentation ??= new EquityIssuerPresentation
        {
            Issuer = issuer,
            EquityIssuerId = issuer.Id,
        };
        issuer.Presentation.Listing = listing;
        issuer.Presentation.EquityListingId = listing.Id;
        return listing;
    }

    public static EquityListing GetOrAddListing(EquityIssuer issuer, string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            throw new ArgumentException(
                "A source-stated listing symbol is required.",
                nameof(ticker)
            );
        var matches = issuer
            .Securities.SelectMany(security => security.Listings)
            .Where(listing => listing.MarketCountryCode == "US" && listing.Ticker == ticker)
            .Take(2)
            .ToList();
        if (matches.Count > 1)
            throw new InvalidOperationException(
                "The source symbol identifies more than one U.S. listing."
            );
        if (matches.Count == 1)
            return matches[0];
        var security = new EquitySecurity { Issuer = issuer, EquityIssuerId = issuer.Id };
        var created = new EquityListing
        {
            Security = security,
            EquitySecurityId = security.Id,
            Ticker = ticker,
            MarketCountryCode = "US",
        };
        security.Listings.Add(created);
        issuer.Securities.Add(security);
        return created;
    }

    public static void ReplaceDirectorySymbols(
        EquityIssuer issuer,
        string primaryTicker,
        IEnumerable<string> secondaryTickers,
        bool activate = false
    )
    {
        var symbols = secondaryTickers.Append(primaryTicker).ToHashSet(StringComparer.Ordinal);
        foreach (var ticker in symbols)
            GetOrAddListing(issuer, ticker);
        foreach (
            var listing in issuer
                .Securities.SelectMany(security => security.Listings)
                .Where(listing => listing.MarketCountryCode == "US")
        )
        {
            listing.IsDirectoryListed = symbols.Contains(listing.Ticker);
            if (listing.IsDirectoryListed && activate)
            {
                listing.Active = true;
                listing.DelistedOn = null;
            }
            else if (!listing.IsDirectoryListed && !listing.IsReferenceListed)
                listing.Active = false;
        }
        SelectPrimary(issuer, primaryTicker);
    }

    public static void ReplaceReferenceSymbols(EquityIssuer issuer, IEnumerable<string> tickers)
    {
        var symbols = tickers.ToHashSet(StringComparer.Ordinal);
        foreach (var ticker in symbols)
            GetOrAddListing(issuer, ticker);
        foreach (
            var listing in issuer
                .Securities.SelectMany(security => security.Listings)
                .Where(listing => listing.MarketCountryCode == "US")
        )
        {
            listing.IsReferenceListed = symbols.Contains(listing.Ticker);
        }
    }
}
