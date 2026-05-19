using System.Reflection;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Repositories.Search;
using Equibles.Search.Abstractions;

namespace Equibles.UnitTests.Search;

/// <summary>
/// Pins CongressMemberSearchProvider.Project (new global search #885, 0%
/// covered). For the hit to become a working profile link, Project must emit
/// Kind "CongressMember" and the member's Guid id under RouteValues key "id" —
/// the exact key SearchCategoryRouteExtensions.HitUrl's "CongressMember" arm
/// reads. Project is protected → reflection (the repo's non-public pattern).
/// </summary>
public class CongressMemberSearchProviderProjectTests
{
    [Fact]
    public void Project_Member_EmitsCongressMemberKindAndGuidIdRouteValue()
    {
        var id = Guid.Parse("11112222-3333-4444-5555-666677778888");
        var provider = new CongressMemberSearchProvider(null);
        var member = new CongressMember { Id = id, Name = "Jane Representative" };

        var project = typeof(CongressMemberSearchProvider).GetMethod(
            "Project",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;

        var hit = (SearchHit)project.Invoke(provider, [member])!;

        hit.Kind.Should().Be("CongressMember");
        hit.RouteValues.Should().ContainKey("id").WhoseValue.Should().Be(id.ToString());
        hit.Title.Should().Be("Jane Representative");
    }
}
