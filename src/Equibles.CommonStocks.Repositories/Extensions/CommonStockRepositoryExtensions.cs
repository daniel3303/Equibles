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
        var stock = await repository.GetByTicker(TickerNormalizer.Normalize(ticker));
        return stock == null ? (null, $"Stock '{ticker}' not found.") : (stock, null);
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
        return repository.GetByIds(ids).Select(s => s.Id).ToHashSetAsync(cancellationToken);
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
