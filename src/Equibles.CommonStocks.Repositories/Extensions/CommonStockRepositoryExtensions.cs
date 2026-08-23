using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories.Extensions;

public static class CommonStockRepositoryExtensions
{
    public static async Task<(CommonStock Stock, string Error)> ResolveByTicker(
        this CommonStockRepository repository,
        string ticker
    )
    {
        var normalized = TickerNormalizer.Normalize(ticker);
        if (normalized == null)
            return (null, $"Stock '{ticker}' not found.");

        var stock = await repository.GetByTicker(normalized);
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
