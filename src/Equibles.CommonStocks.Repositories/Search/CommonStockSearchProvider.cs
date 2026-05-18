using Equibles.CommonStocks.Data.Models;
using Equibles.Search.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories.Search;

/// <summary>Stocks group of the global search. Wraps the existing ticker/name search.</summary>
public class CommonStockSearchProvider : QueryableSearchProvider<CommonStock>
{
    private readonly CommonStockRepository _commonStockRepository;

    public CommonStockSearchProvider(CommonStockRepository commonStockRepository)
    {
        _commonStockRepository = commonStockRepository;
    }

    public override string Category => "Stocks";

    public override int Order => 0;

    protected override IQueryable<CommonStock> Filter(SearchRequest request) =>
        _commonStockRepository.Search(request.Query);

    protected override Task<List<CommonStock>> Materialize(
        IQueryable<CommonStock> query,
        CancellationToken cancellationToken
    ) => query.ToListAsync(cancellationToken);

    protected override SearchHit Project(CommonStock stock) =>
        new()
        {
            Title = stock.Ticker,
            Subtitle = stock.Name,
            Kind = "Stock",
            RouteValues = { ["ticker"] = stock.Ticker },
        };
}
