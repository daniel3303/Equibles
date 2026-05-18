using Equibles.Congress.Data.Models;
using Equibles.Search.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Repositories.Search;

/// <summary>Congress group of the global search. Wraps the existing member name search.</summary>
public class CongressMemberSearchProvider : QueryableSearchProvider<CongressMember>
{
    private readonly CongressMemberRepository _congressMemberRepository;

    public CongressMemberSearchProvider(CongressMemberRepository congressMemberRepository)
    {
        _congressMemberRepository = congressMemberRepository;
    }

    public override string Category => "Congress";

    public override int Order => 50;

    protected override IQueryable<CongressMember> Filter(SearchRequest request) =>
        _congressMemberRepository.Search(request.Query).OrderBy(member => member.Name);

    protected override Task<List<CongressMember>> Materialize(
        IQueryable<CongressMember> query,
        CancellationToken cancellationToken
    ) => query.ToListAsync(cancellationToken);

    protected override SearchHit Project(CongressMember member) =>
        new()
        {
            Title = member.Name,
            Kind = "CongressMember",
            RouteValues = { ["id"] = member.Id.ToString() },
        };
}
