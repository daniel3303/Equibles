using Equibles.Search.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories.Search;

/// <summary>Stocks group of the global search. Wraps the existing ticker/name search.</summary>
public class CommonStockSearchProvider : ISearchProvider
{
    private readonly CommonStockRepository _commonStockRepository;

    public CommonStockSearchProvider(CommonStockRepository commonStockRepository)
    {
        _commonStockRepository = commonStockRepository;
    }

    public string Category => "Stocks";

    public int Order => 0;

    public async Task<SearchResultGroup> Search(
        SearchRequest request,
        CancellationToken cancellationToken
    )
    {
        var stocks = await _commonStockRepository
            .Search(request.Query)
            .Take(request.MaxPerProvider)
            .Select(stock => new { stock.Ticker, stock.Name })
            .ToListAsync(cancellationToken);

        return new SearchResultGroup
        {
            Category = Category,
            Order = Order,
            Hits = stocks
                .Select(stock => new SearchHit
                {
                    Title = stock.Ticker,
                    Subtitle = stock.Name,
                    Kind = "Stock",
                    RouteValues = { ["ticker"] = stock.Ticker },
                })
                .ToList(),
        };
    }
}
