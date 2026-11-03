using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Worker;

[Service]
public class TickerMapService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TickerMapService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Maps stored primary tickers to stock ids. The default comparer is case-insensitive for
    /// sources that vary letter case of the SAME security's symbol. Sources whose casing is
    /// itself identity — FINRA writes preferred/when-issued suffixes in lowercase (TpC is a
    /// DIFFERENT security from TPC) — must pass <see cref="StringComparer.Ordinal"/>, or the
    /// case-fold silently merges two securities onto one stock.
    /// </summary>
    public async Task<Dictionary<string, Guid>> Build(
        List<string> tickersToSync,
        CancellationToken cancellationToken,
        StringComparer comparer = null
    )
    {
        using var scope = _scopeFactory.CreateScope();
        EquityIssuerRepository stockRepo =
            scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();

        var query =
            tickersToSync?.Count > 0
                ? stockRepo.GetUsByTickers(tickersToSync)
                : stockRepo.GetCurrentUsDirectory();
        var delistedListings = stockRepo.GetDelistedListings();
        query = query.Where(stock =>
            !delistedListings.Any(listing =>
                listing.EquityIssuerId == stock.Id
                && listing.ListedTicker == stock.Presentation.Listing.Ticker
            )
        );

        return await query.ToDictionaryAsync(
            s => s.Presentation.Listing.Ticker,
            s => s.Id,
            comparer ?? StringComparer.OrdinalIgnoreCase,
            cancellationToken
        );
    }

    /// <summary>
    /// Maps every current primary or authoritative exchange-traded reference ticker to its exact
    /// listing identity. Duplicate ticker claims fail closed instead of selecting an arbitrary filer.
    /// </summary>
    public async Task<Dictionary<string, ListedSecurityKey>> BuildListed(
        List<string> tickersToSync,
        CancellationToken cancellationToken,
        StringComparer comparer = null
    )
    {
        using var scope = _scopeFactory.CreateScope();
        EquityIssuerRepository stockRepo =
            scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();
        var stocks = await stockRepo
            .GetCurrentUsDirectory()
            .Where(stock => stock.Presentation.Listing.Active)
            .Select(stock => new
            {
                stock.Id,
                Ticker = stock.Presentation.Listing.Ticker,
                ReferenceTickers = stock
                    .Securities.SelectMany(nativeSecurity => nativeSecurity.Listings)
                    .Where(nativeListing =>
                        nativeListing.MarketCountryCode == "US" && (nativeListing.IsReferenceListed)
                    )
                    .Select(nativeListing => nativeListing.Ticker)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);
        var rawDelisted = await stockRepo
            .GetDelistedListings()
            .Select(listing => new ListedSecurityKey(listing.EquityIssuerId, listing.ListedTicker))
            .ToListAsync(cancellationToken);
        var primaryByStock = stocks.ToDictionary(stock => stock.Id, stock => stock.Ticker);
        var delistedSet = rawDelisted
            .Select(listing =>
            {
                var primary = primaryByStock.GetValueOrDefault(listing.CommonStockId);
                var canonical =
                    primary != null
                    && string.Equals(
                        TickerNormalizer.NormalizeDashListed(listing.ListedTicker),
                        TickerNormalizer.NormalizeDashListed(primary),
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? primary
                        : listing.ListedTicker;
                return new ListedSecurityKey(listing.CommonStockId, canonical);
            })
            .ToHashSet();
        var requested =
            tickersToSync?.Count > 0
                ? tickersToSync.ToHashSet(comparer ?? StringComparer.OrdinalIgnoreCase)
                : null;
        var claims = stocks
            .SelectMany(stock =>
                new[] { stock.Ticker }
                    .Concat(stock.ReferenceTickers ?? [])
                    .Where(ticker => !string.IsNullOrWhiteSpace(ticker))
                    .Distinct(comparer ?? StringComparer.OrdinalIgnoreCase)
                    .Select(ticker => new KeyValuePair<string, ListedSecurityKey>(
                        ticker,
                        new ListedSecurityKey(
                            stock.Id,
                            string.Equals(
                                TickerNormalizer.NormalizeDashListed(ticker),
                                TickerNormalizer.NormalizeDashListed(stock.Ticker),
                                StringComparison.OrdinalIgnoreCase
                            )
                                ? stock.Ticker
                                : ticker
                        )
                    ))
            )
            .Where(claim => requested == null || requested.Contains(claim.Key))
            .Where(claim => !delistedSet.Contains(claim.Value))
            .GroupBy(claim => claim.Key, comparer ?? StringComparer.OrdinalIgnoreCase)
            .Where(group =>
                group.Select(claim => claim.Value.CommonStockId).Distinct().Count() == 1
            )
            .Select(group => group.First());

        return claims.ToDictionary(
            claim => claim.Key,
            claim => claim.Value,
            comparer ?? StringComparer.OrdinalIgnoreCase
        );
    }

    public async Task<Dictionary<string, EquityListingReference>> BuildNativeListed(
        List<string> tickersToSync,
        CancellationToken cancellationToken,
        StringComparer comparer = null
    )
    {
        var source = await BuildListed(tickersToSync, cancellationToken, comparer);
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<EquityListingRepository>();
        var issuerIds = source.Values.Select(row => row.CommonStockId).Distinct().ToList();
        var candidates = await repository
            .GetAll()
            .Where(listing =>
                listing.MarketCountryCode == "US"
                && issuerIds.Contains(listing.Security.EquityIssuerId)
            )
            .Select(listing => new EquityListingReference(
                listing.Id,
                listing.Security.EquityIssuerId,
                listing.Ticker
            ))
            .ToListAsync(cancellationToken);
        var mappings = candidates
            .GroupBy(listing => new ListedSecurityKey(listing.EquityIssuerId, listing.ListedTicker))
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
        return source.ToDictionary(
            row => row.Key,
            row =>
                mappings.TryGetValue(row.Value, out var listing)
                    ? listing
                    : throw new InvalidOperationException(
                        $"No native identity for {row.Value.CommonStockId}/{row.Value.ListedTicker}."
                    ),
            comparer ?? StringComparer.OrdinalIgnoreCase
        );
    }

    /// <summary>
    /// Maps each current filer's other active, directory-listed US line (a second share class, a
    /// preferred, a warrant) to its exact listing, for FINRA feeds that report every line under
    /// its own symbol. Callers match these keys by exact or dotted spelling only: no authoritative
    /// security type tells the lines apart, and a compressed preferred spelling can be another
    /// company's symbol. A ticker already claimed, or held by any other active US line, is dropped.
    /// </summary>
    public async Task<Dictionary<string, EquityListingReference>> BuildSecondaryNativeListed(
        ICollection<string> claimedTickers,
        List<string> tickersToSync,
        CancellationToken cancellationToken,
        StringComparer comparer = null
    )
    {
        comparer ??= StringComparer.OrdinalIgnoreCase;
        using var scope = _scopeFactory.CreateScope();
        var issuerRepository = scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();
        var listingRepository = scope.ServiceProvider.GetRequiredService<EquityListingRepository>();
        var presentations = await issuerRepository
            .GetCurrentUsDirectory()
            .Where(issuer => issuer.Presentation.Listing.Active)
            .Select(issuer => new
            {
                issuer.Id,
                issuer.Presentation.EquityListingId,
                issuer.Presentation.Listing.Ticker,
            })
            .ToDictionaryAsync(issuer => issuer.Id, cancellationToken);
        var activeUsLines = await listingRepository
            .GetActiveUsListings()
            .Where(listing => listing.Ticker != null && listing.Ticker != "")
            .Select(listing => new
            {
                listing.Id,
                listing.Security.EquityIssuerId,
                listing.Ticker,
                listing.IsDirectoryListed,
                listing.IsReferenceListed,
            })
            .ToListAsync(cancellationToken);
        var delisted = (
            await issuerRepository
                .GetDelistedListings()
                .Select(listing => new { listing.EquityIssuerId, listing.ListedTicker })
                .ToListAsync(cancellationToken)
        )
            .Select(listing => new ListedSecurityKey(
                listing.EquityIssuerId,
                TickerNormalizer.NormalizeDashListed(listing.ListedTicker)
            ))
            .ToHashSet();
        var linesPerTicker = activeUsLines
            .GroupBy(line => line.Ticker, comparer)
            .ToDictionary(group => group.Key, group => group.Count(), comparer);
        var requested = tickersToSync?.Count > 0 ? tickersToSync.ToHashSet(comparer) : null;
        return activeUsLines
            .Where(line =>
                line.IsDirectoryListed
                && !line.IsReferenceListed
                && presentations.TryGetValue(line.EquityIssuerId, out var presentation)
                && line.Id != presentation.EquityListingId
                // A dotted or dashed spelling of the presentation ticker is that same line.
                && !string.Equals(
                    TickerNormalizer.NormalizeDashListed(line.Ticker),
                    TickerNormalizer.NormalizeDashListed(presentation.Ticker),
                    StringComparison.OrdinalIgnoreCase
                )
                && linesPerTicker[line.Ticker] == 1
                && !claimedTickers.Contains(line.Ticker)
                && (requested == null || requested.Contains(line.Ticker))
                && !delisted.Contains(
                    new ListedSecurityKey(
                        line.EquityIssuerId,
                        TickerNormalizer.NormalizeDashListed(line.Ticker)
                    )
                )
            )
            .ToDictionary(
                line => line.Ticker,
                line => new EquityListingReference(line.Id, line.EquityIssuerId, line.Ticker),
                comparer
            );
    }
}
