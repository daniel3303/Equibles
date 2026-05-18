using Equibles.Search.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Repositories.Search;

/// <summary>Insiders group of the global search. Wraps the existing owner name search.</summary>
public class InsiderOwnerSearchProvider : ISearchProvider
{
    private readonly InsiderOwnerRepository _insiderOwnerRepository;

    public InsiderOwnerSearchProvider(InsiderOwnerRepository insiderOwnerRepository)
    {
        _insiderOwnerRepository = insiderOwnerRepository;
    }

    public string Category => "Insiders";

    public int Order => 40;

    public async Task<SearchResultGroup> Search(
        SearchRequest request,
        CancellationToken cancellationToken
    )
    {
        var owners = await _insiderOwnerRepository
            .Search(request.Query)
            .OrderBy(owner => owner.Name)
            .Take(request.MaxPerProvider)
            .Select(owner => new
            {
                owner.Name,
                owner.OwnerCik,
                owner.OfficerTitle,
                owner.IsDirector,
                owner.IsTenPercentOwner,
            })
            .ToListAsync(cancellationToken);

        return new SearchResultGroup
        {
            Category = Category,
            Order = Order,
            Hits = owners
                .Select(owner => new SearchHit
                {
                    Title = owner.Name,
                    Subtitle = DescribeRole(
                        owner.OfficerTitle,
                        owner.IsDirector,
                        owner.IsTenPercentOwner
                    ),
                    Kind = "Insider",
                    RouteValues = { ["ownerCik"] = owner.OwnerCik },
                })
                .ToList(),
        };
    }

    private static string DescribeRole(string officerTitle, bool isDirector, bool isTenPercentOwner)
    {
        if (!string.IsNullOrWhiteSpace(officerTitle))
        {
            return officerTitle;
        }

        if (isDirector)
        {
            return "Director";
        }

        if (isTenPercentOwner)
        {
            return "10% owner";
        }

        return null;
    }
}
