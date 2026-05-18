using Equibles.InsiderTrading.Data.Models;
using Equibles.Search.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Repositories.Search;

/// <summary>Insiders group of the global search. Wraps the existing owner name search.</summary>
public class InsiderOwnerSearchProvider : QueryableSearchProvider<InsiderOwner>
{
    private readonly InsiderOwnerRepository _insiderOwnerRepository;

    public InsiderOwnerSearchProvider(InsiderOwnerRepository insiderOwnerRepository)
    {
        _insiderOwnerRepository = insiderOwnerRepository;
    }

    public override string Category => "Insiders";

    public override int Order => 40;

    protected override IQueryable<InsiderOwner> Filter(SearchRequest request) =>
        _insiderOwnerRepository.Search(request.Query).OrderBy(owner => owner.Name);

    protected override Task<List<InsiderOwner>> Materialize(
        IQueryable<InsiderOwner> query,
        CancellationToken cancellationToken
    ) => query.ToListAsync(cancellationToken);

    protected override SearchHit Project(InsiderOwner owner) =>
        new()
        {
            Title = owner.Name,
            Subtitle = DescribeRole(owner.OfficerTitle, owner.IsDirector, owner.IsTenPercentOwner),
            Kind = "Insider",
            RouteValues = { ["ownerCik"] = owner.OwnerCik },
        };

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
