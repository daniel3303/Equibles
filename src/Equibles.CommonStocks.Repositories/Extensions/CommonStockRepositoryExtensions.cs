using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories.Extensions;

public static class CommonStockRepositoryExtensions
{
    /// <summary>
    /// The current exact listing identities whose authoritative ticker claim resolves to one
    /// active filer. Market-wide readers use this after materializing source rows so a stale row
    /// cannot publish a ticker that became delisted or ambiguously claimed after ingestion.
    /// </summary>
    public static async Task<HashSet<ListedSecurityKey>> GetUniqueActiveListingKeys(
        this CommonStockRepository repository,
        CancellationToken cancellationToken = default
    )
    {
        var stocks = await repository
            .GetAll()
            .Select(stock => new
            {
                stock.Id,
                stock.Ticker,
                stock.ReferenceTickers,
            })
            .ToListAsync(cancellationToken);
        var stockIds = stocks.Select(stock => stock.Id).ToList();
        var delistedRows = await repository
            .GetDelistedListings()
            .Where(listing => stockIds.Contains(listing.CommonStockId))
            .Select(listing => new ListedSecurityKey(listing.CommonStockId, listing.ListedTicker))
            .ToListAsync(cancellationToken);
        var primaryByStock = stocks.ToDictionary(stock => stock.Id, stock => stock.Ticker);
        var delisted = delistedRows
            .Select(listing =>
            {
                var primary = primaryByStock.GetValueOrDefault(listing.CommonStockId);
                var listedTicker =
                    primary != null
                    && string.Equals(
                        TickerNormalizer.NormalizeDashListed(listing.ListedTicker),
                        TickerNormalizer.NormalizeDashListed(primary),
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? primary
                        : listing.ListedTicker;
                return new ListedSecurityKey(listing.CommonStockId, listedTicker);
            })
            .ToHashSet();

        return stocks
            .SelectMany(stock =>
                new[] { stock.Ticker }
                    .Concat(stock.ReferenceTickers ?? [])
                    .Where(ticker => !string.IsNullOrWhiteSpace(ticker))
                    .Distinct(StringComparer.Ordinal)
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
            .Where(claim => !delisted.Contains(claim.Value))
            .GroupBy(claim => claim.Key, StringComparer.Ordinal)
            .Where(group =>
                group.Select(claim => claim.Value.CommonStockId).Distinct().Count() == 1
            )
            .SelectMany(group => group.Select(claim => claim.Value))
            .ToHashSet();
    }

    public static async Task<(CommonStock Stock, string Error)> ResolveByTicker(
        this CommonStockRepository repository,
        string ticker
    )
    {
        var normalized = TickerNormalizer.Normalize(ticker);
        if (normalized == null)
            return (null, $"Stock '{ticker}' not found.");

        var literal = normalized;
        var folded = TickerNormalizer.NormalizeDashListed(normalized) ?? literal;

        async Task<List<CommonStock>> FindOwners(string listedTicker) =>
            await repository
                .GetAll()
                .Where(candidate =>
                    candidate.Ticker == listedTicker
                    || (candidate.Active && candidate.ReferenceTickers.Contains(listedTicker))
                )
                .Take(2)
                .ToListAsync();

        var authoritativeOwners = await FindOwners(literal);
        if (authoritativeOwners.Select(candidate => candidate.Id).Distinct().Count() > 1)
            return (null, $"Listed security '{ticker}' is ambiguous.");
        var stock = authoritativeOwners.SingleOrDefault();
        if (stock == null && !string.Equals(literal, folded, StringComparison.OrdinalIgnoreCase))
        {
            authoritativeOwners = await FindOwners(folded);
            if (authoritativeOwners.Select(candidate => candidate.Id).Distinct().Count() > 1)
                return (null, $"Listed security '{ticker}' is ambiguous.");
            stock = authoritativeOwners.SingleOrDefault();
        }
        stock ??= await repository.GetByTicker(normalized);
        if (stock == null && normalized.Contains('.'))
            stock = await repository.GetByTicker(normalized.Replace('.', '-'));
        return stock == null ? (null, $"Stock '{ticker}' not found.") : (stock, null);
    }

    // SEC CIKs appear padded and unpadded, while a surviving filer can also own a predecessor's
    // CIK through SecondaryCiks. Resolve the canonical identity across both fields and fail closed
    // when corrupted ownership maps the same CIK to more than one CommonStock.
    public static async Task<CommonStock> GetByCikTolerant(
        this CommonStockRepository repository,
        string cik,
        CancellationToken cancellationToken = default
    )
    {
        var validated = CikNormalizer.Validate(cik);
        var canonical = CikNormalizer.Canonicalize(validated);
        if (canonical == null)
            return null;

        var padded = canonical.PadLeft(10, '0');
        var matches = await repository
            .GetAll()
            .Where(stock =>
                stock.Cik == validated
                || stock.Cik == canonical
                || stock.Cik == padded
                || stock.SecondaryCiks.Contains(validated)
                || stock.SecondaryCiks.Contains(canonical)
                || stock.SecondaryCiks.Contains(padded)
            )
            .Take(2)
            .ToListAsync(cancellationToken);
        return matches.Count == 1 ? matches[0] : null;
    }

    // Returns the subset of the given ids whose CommonStock still exists. Importers
    // re-validate batches against this before insert because a parallel CompanySync can
    // hard-delete a stock after a ticker map is built, and a dangling FK rolls back the
    // whole batch.
    public static Task<HashSet<Guid>> GetExistingIds(
        this CommonStockRepository repository,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        return repository
            .GetByIdsIncludingInactive(ids)
            .Select(s => s.Id)
            .ToHashSetAsync(cancellationToken);
    }

    // Returns the subset of items whose CommonStockId still exists, preserving order.
    // Importers filter each batch through this before insert because a parallel CompanySync
    // can hard-delete a stock after a ticker map is built, and a single dangling FK rolls
    // back the whole batch.
    public static async Task<List<T>> FilterByExistingStocks<T>(
        this CommonStockRepository repository,
        List<T> items,
        Func<T, Guid> stockIdSelector,
        CancellationToken cancellationToken = default
    )
    {
        var liveStockIds = await repository.GetExistingIds(
            items.Select(stockIdSelector).Distinct(),
            cancellationToken
        );
        return items.Where(i => liveStockIds.Contains(stockIdSelector(i))).ToList();
    }
}
