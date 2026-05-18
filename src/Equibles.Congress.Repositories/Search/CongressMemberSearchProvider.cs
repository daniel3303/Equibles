using Equibles.Search.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Repositories.Search;

/// <summary>Congress group of the global search. Wraps the existing member name search.</summary>
public class CongressMemberSearchProvider : ISearchProvider
{
    private readonly CongressMemberRepository _congressMemberRepository;

    public CongressMemberSearchProvider(CongressMemberRepository congressMemberRepository)
    {
        _congressMemberRepository = congressMemberRepository;
    }

    public string Category => "Congress";

    public int Order => 50;

    public async Task<SearchResultGroup> Search(
        SearchRequest request,
        CancellationToken cancellationToken
    )
    {
        var members = await _congressMemberRepository
            .Search(request.Query)
            .OrderBy(member => member.Name)
            .Take(request.MaxPerProvider)
            .Select(member => new { member.Name })
            .ToListAsync(cancellationToken);

        return new SearchResultGroup
        {
            Category = Category,
            Order = Order,
            Hits = members
                .Select(member => new SearchHit
                {
                    Title = member.Name,
                    Kind = "CongressMember",
                    RouteValues = { ["name"] = member.Name },
                })
                .ToList(),
        };
    }
}
