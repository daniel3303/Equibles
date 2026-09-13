using Equibles.CommonStocks.Data.Models;
using Equibles.Search.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories.Search;

/// <summary>Stocks group of the global search. Wraps the existing ticker/name search.</summary>
public class CommonStockSearchProvider : QueryableSearchProvider<EquityIssuer>
{
    private readonly EquityIssuerRepository _commonStockRepository;

    public CommonStockSearchProvider(EquityIssuerRepository commonStockRepository)
    {
        _commonStockRepository = commonStockRepository;
    }

    public override string Category => "Stocks";

    public override int Order => 0;

    protected override IQueryable<EquityIssuer> Filter(SearchRequest request) =>
        _commonStockRepository.Search(request.Query);

    protected override Task<List<EquityIssuer>> Materialize(
        IQueryable<EquityIssuer> query,
        CancellationToken cancellationToken
    ) => query.ToListAsync(cancellationToken);

    protected override SearchHit Project(EquityIssuer stock) =>
        new()
        {
            Title = stock.Presentation.Listing.Ticker,
            Subtitle = stock.Name,
            Kind = "Stock",
            RouteValues = { ["ticker"] = stock.Presentation.Listing.Ticker },
        };
}
