using Equibles.Cftc.Data.Models;
using Equibles.Search.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Cftc.Repositories.Search;

/// <summary>Futures group of the global search. Wraps the existing market code/name search.</summary>
public class CftcContractSearchProvider : QueryableSearchProvider<CftcContract>
{
    private readonly CftcContractRepository _cftcContractRepository;

    public CftcContractSearchProvider(CftcContractRepository cftcContractRepository)
    {
        _cftcContractRepository = cftcContractRepository;
    }

    public override string Category => "Futures";

    public override int Order => 60;

    protected override IQueryable<CftcContract> Filter(SearchRequest request) =>
        _cftcContractRepository.Search(request.Query).OrderBy(contract => contract.MarketName);

    protected override Task<List<CftcContract>> Materialize(
        IQueryable<CftcContract> query,
        CancellationToken cancellationToken
    ) => query.ToListAsync(cancellationToken);

    protected override SearchHit Project(CftcContract contract) =>
        new()
        {
            Title = contract.MarketName,
            Subtitle = contract.MarketCode,
            Kind = "FuturesMarket",
            RouteValues = { ["marketCode"] = contract.MarketCode },
        };
}
