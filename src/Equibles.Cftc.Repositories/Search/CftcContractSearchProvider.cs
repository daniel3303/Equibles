using Equibles.Search.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Cftc.Repositories.Search;

/// <summary>Futures group of the global search. Wraps the existing market code/name search.</summary>
public class CftcContractSearchProvider : ISearchProvider
{
    private readonly CftcContractRepository _cftcContractRepository;

    public CftcContractSearchProvider(CftcContractRepository cftcContractRepository)
    {
        _cftcContractRepository = cftcContractRepository;
    }

    public string Category => "Futures";

    public int Order => 60;

    public async Task<SearchResultGroup> Search(
        SearchRequest request,
        CancellationToken cancellationToken
    )
    {
        var contracts = await _cftcContractRepository
            .Search(request.Query)
            .OrderBy(contract => contract.MarketName)
            .Take(request.MaxPerProvider)
            .Select(contract => new { contract.MarketCode, contract.MarketName })
            .ToListAsync(cancellationToken);

        return new SearchResultGroup
        {
            Category = Category,
            Order = Order,
            Hits = contracts
                .Select(contract => new SearchHit
                {
                    Title = contract.MarketName,
                    Subtitle = contract.MarketCode,
                    Kind = "FuturesMarket",
                    RouteValues = { ["marketCode"] = contract.MarketCode },
                })
                .ToList(),
        };
    }
}
