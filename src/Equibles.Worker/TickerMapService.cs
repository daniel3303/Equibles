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
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var query =
            tickersToSync?.Count > 0 ? stockRepo.GetByTickers(tickersToSync) : stockRepo.GetAll();
        var delistedListings = stockRepo.GetDelistedListings();
        query = query.Where(stock =>
            !delistedListings.Any(listing =>
                listing.CommonStockId == stock.Id && listing.ListedTicker == stock.Ticker
            )
        );

        return await query.ToDictionaryAsync(
            s => s.Ticker,
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
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var stocks = await stockRepo
            .GetAll()
            .Where(stock => stock.Active)
            .Select(stock => new
            {
                stock.Id,
                stock.Ticker,
                stock.ReferenceTickers,
            })
            .ToListAsync(cancellationToken);
        var rawDelisted = await stockRepo
            .GetDelistedListings()
            .Select(listing => new ListedSecurityKey(listing.CommonStockId, listing.ListedTicker))
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
}
