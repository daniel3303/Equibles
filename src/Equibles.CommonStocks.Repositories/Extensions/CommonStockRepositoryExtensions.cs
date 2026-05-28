using Equibles.CommonStocks.Data.Models;

namespace Equibles.CommonStocks.Repositories.Extensions;

public static class CommonStockRepositoryExtensions
{
    public static async Task<(CommonStock Stock, string Error)> ResolveByTicker(
        this CommonStockRepository repository,
        string ticker
    )
    {
        var stock = await repository.GetByTicker(ticker.Trim().ToUpperInvariant());
        return stock == null ? (null, $"Stock '{ticker}' not found.") : (stock, null);
    }
}
