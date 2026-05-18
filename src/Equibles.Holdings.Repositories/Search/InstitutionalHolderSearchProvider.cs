using Equibles.Holdings.Data.Models;
using Equibles.Search.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories.Search;

/// <summary>Institutions group of the global search. Wraps the existing holder name search.</summary>
public class InstitutionalHolderSearchProvider : QueryableSearchProvider<InstitutionalHolder>
{
    private readonly InstitutionalHolderRepository _institutionalHolderRepository;

    public InstitutionalHolderSearchProvider(
        InstitutionalHolderRepository institutionalHolderRepository
    )
    {
        _institutionalHolderRepository = institutionalHolderRepository;
    }

    public override string Category => "Institutions";

    public override int Order => 30;

    protected override IQueryable<InstitutionalHolder> Filter(SearchRequest request) =>
        _institutionalHolderRepository.Search(request.Query).OrderBy(holder => holder.Name);

    protected override Task<List<InstitutionalHolder>> Materialize(
        IQueryable<InstitutionalHolder> query,
        CancellationToken cancellationToken
    ) => query.ToListAsync(cancellationToken);

    protected override SearchHit Project(InstitutionalHolder holder) =>
        new()
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
        };
}
