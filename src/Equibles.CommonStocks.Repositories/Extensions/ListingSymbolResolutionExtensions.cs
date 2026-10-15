using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.EquityMarkets.Data.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories.Extensions;

/// <summary>
/// Turns the symbol a caller typed into an issuer and, for a listing outside the US, the exact venue
/// listing it names. A bare ticker keeps its US meaning, so AIR stays the US filer and only XPAR:AIR
/// reaches Euronext Paris.
/// </summary>
public static class ListingSymbolResolutionExtensions
{
    /// <summary>
    /// A bare ticker resolves exactly as <see cref="CommonStockRepositoryExtensions.ResolveByTicker"/>
    /// and returns no venue listing. <c>MIC:TICKER</c> names one active, verified venue listing. A
    /// provider suffix (<c>MC.PA</c>) is read only after the US lookup misses, so a US ticker that
    /// carries a dot always wins.
    /// </summary>
    public static async Task<(
        EquityIssuer Stock,
        EquityListing Venue,
        string Error
    )> ResolveListing(this EquityIssuerRepository repository, string symbol)
    {
        if (EquityListingSymbol.TryParse(symbol, out var code, out var ticker))
            return await repository.ResolveVenueListing(symbol.Trim(), [code], ticker);

        var (stock, error) = await repository.ResolveByTicker(symbol);
        if (stock != null)
            return (stock, null, null);

        if (TrySplitSuffix(symbol, out var codes, out var suffixed))
        {
            var venue = await repository.ResolveVenueListing(symbol.Trim(), codes, suffixed);
            if (venue.Stock != null)
                return venue;
        }
        return (null, null, error);
    }

    /// <summary>
    /// <see cref="CommonStockRepositoryExtensions.ResolveByTickerIncludingDelisted"/> that also accepts
    /// a venue symbol, for readers keyed by the issuer alone (financial statements and facts).
    /// </summary>
    public static async Task<(EquityIssuer Stock, string Error)> ResolveIssuerIncludingDelisted(
        this EquityIssuerRepository repository,
        string symbol
    )
    {
        if (EquityListingSymbol.TryParse(symbol, out _, out _))
        {
            var (venueStock, _, venueError) = await repository.ResolveListing(symbol);
            return (venueStock, venueError);
        }

        var (stock, error) = await repository.ResolveByTickerIncludingDelisted(symbol);
        if (stock != null || symbol == null || !symbol.Contains('.'))
            return (stock, error);
        var suffixed = await repository.ResolveListing(symbol);
        return suffixed.Venue != null ? (suffixed.Stock, null) : (null, error);
    }

    private static async Task<(
        EquityIssuer Stock,
        EquityListing Venue,
        string Error
    )> ResolveVenueListing(
        this EquityIssuerRepository repository,
        string symbol,
        string[] codes,
        string ticker
    )
    {
        var listings = repository
            .GetAll()
            .SelectMany(issuer => issuer.Securities.SelectMany(security => security.Listings))
            .Where(listing => listing.Active && codes.Contains(listing.MarketIdentifierCode));

        foreach (var spelling in VenueSpellings(codes, ticker))
        {
            var claims = await listings
                .Where(listing => listing.Ticker == spelling)
                .Take(2)
                .ToListAsync();
            // A current claim shadows an alias even when conflicting claims make it ambiguous.
            if (claims.Count == 0)
                claims = await listings
                    .Where(listing =>
                        listing.IdentityState == EquityIdentityState.Verified
                        && listing.TickerAliases.Any(alias => alias.Ticker == spelling)
                    )
                    .Take(2)
                    .ToListAsync();
            if (claims.Count > 1)
                return (null, null, $"Listing '{symbol}' is ambiguous.");
            if (claims.Count == 1)
                return
                    claims[0].IdentityState == EquityIdentityState.Verified
                    && claims[0].MarketCountryCode != "US"
                    ? (claims[0].Security.Issuer, claims[0], null)
                    : (null, null, VenueNotFound(symbol));
        }
        return (null, null, VenueNotFound(symbol));
    }

    // The spellings a venue stores for what a caller typed: the directory writes a class separator as a
    // hyphen and drops London's trailing dot, and a numeric market pads its codes to a fixed width.
    private static IEnumerable<string> VenueSpellings(string[] codes, string ticker)
    {
        var tokens = ticker.TrimEnd('.').Split([' ', '.', '-'], StringSplitOptions.None);
        var directory = tokens.Any(token => token.Length == 0)
            ? null
            : TickerNormalizer.NormalizeListed(string.Join('-', tokens));
        var width = codes
            .Select(code => EquityMarketCatalog.ByMarketIdentifierCode(code)?.NumericTickerWidth)
            .FirstOrDefault(candidate => candidate != null);
        var padded =
            width != null && ticker.Length < width && ticker.All(char.IsAsciiDigit)
                ? ticker.PadLeft(width.Value, '0')
                : null;

        return new[] { ticker, directory, padded }
            .Where(spelling => spelling != null)
            .Distinct(StringComparer.Ordinal);
    }

    private static bool TrySplitSuffix(string symbol, out string[] codes, out string ticker)
    {
        codes = null;
        ticker = null;
        var trimmed = symbol?.Trim();
        var dot = trimmed?.LastIndexOf('.') ?? -1;
        if (dot <= 0)
            return false;

        var suffix = trimmed[dot..];
        codes = EquityMarketCatalog
            .All.Where(market =>
                string.Equals(market.YahooSuffix, suffix, StringComparison.OrdinalIgnoreCase)
            )
            .SelectMany(market => market.MarketIdentifierCodes)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        ticker = TickerNormalizer.NormalizeListed(trimmed[..dot]);
        return codes.Length > 0 && ticker != null;
    }

    private static string VenueNotFound(string symbol) =>
        $"Listing '{symbol}' not found. Use a bare ticker for a US listing, or MIC:TICKER "
        + "(for example XPAR:AIR or XLON:BP) for a listing on another exchange.";
}
