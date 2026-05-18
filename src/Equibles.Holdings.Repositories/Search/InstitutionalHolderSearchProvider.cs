using Equibles.Search.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories.Search;

/// <summary>Institutions group of the global search. Wraps the existing holder name search.</summary>
public class InstitutionalHolderSearchProvider : ISearchProvider
{
    private readonly InstitutionalHolderRepository _institutionalHolderRepository;

    public InstitutionalHolderSearchProvider(
        InstitutionalHolderRepository institutionalHolderRepository
    )
    {
        _institutionalHolderRepository = institutionalHolderRepository;
    }

    public string Category => "Institutions";

    public int Order => 30;

    public async Task<SearchResultGroup> Search(
        SearchRequest request,
        CancellationToken cancellationToken
    )
    {
        var holders = await _institutionalHolderRepository
            .Search(request.Query)
            .OrderBy(holder => holder.Name)
            .Take(request.MaxPerProvider)
            .Select(holder => new
            {
                holder.Name,
                holder.Cik,
                holder.City,
                holder.StateOrCountry,
            })
            .ToListAsync(cancellationToken);

        return new SearchResultGroup
        {
            Category = Category,
            Order = Order,
            Hits = holders
                .Select(holder => new SearchHit
                {
                    Title = holder.Name,
                    Subtitle = string.Join(
                        ", ",
                        new[] { holder.City, holder.StateOrCountry }.Where(part =>
                            !string.IsNullOrWhiteSpace(part)
                        )
                    ),
                    Kind = "Institution",
                    RouteValues = { ["cik"] = holder.Cik },
                })
                .ToList(),
        };
    }
}
