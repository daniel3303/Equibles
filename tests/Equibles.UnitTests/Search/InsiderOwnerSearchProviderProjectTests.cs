using System.Reflection;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories.Search;
using Equibles.Search.Abstractions;

namespace Equibles.UnitTests.Search;

/// <summary>
/// Pins InsiderOwnerSearchProvider.Project (new global search #885, 0% covered).
/// The four existing DescribeRole sibling tests pin the role-string fallback chain,
/// but none of them go through Project itself — so the cross-component wiring
/// HitUrl's "Insider" arm relies on (Kind + RouteValues["ownerCik"]) has no
/// producer-side guard. A rename on either side ships a dead link with no
/// compile-time check. Project is protected → reflection (repo pattern; the
/// repository dependency is unused by Project).
///
/// Adversarial input choice: an owner with OfficerTitle = null AND IsDirector =
/// false AND IsTenPercentOwner = false — this drives DescribeRole through every
/// fallback to its only currently-uncovered exit (the `return null;` line). The
/// Subtitle should be null per the documented contract; a regression treating
/// it as "" or "Unknown" would compile but break the empty-role rendering.
/// </summary>
public class InsiderOwnerSearchProviderProjectTests
{
    [Fact]
    public void Project_NoRoleFlagsSet_SubtitleNullAndWiresInsiderRoute()
    {
        var provider = new InsiderOwnerSearchProvider(null);
        var owner = new InsiderOwner
        {
            OwnerCik = "0001316889",
            Name = "Buffett Warren E",
            OfficerTitle = null,
            IsDirector = false,
            IsTenPercentOwner = false,
        };

        var project = typeof(InsiderOwnerSearchProvider).GetMethod(
            "Project",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;

        var hit = (SearchHit)project.Invoke(provider, [owner])!;

        hit.Subtitle.Should().BeNull();
        hit.Kind.Should().Be("Insider");
        hit.RouteValues.Should().ContainKey("ownerCik").WhoseValue.Should().Be("0001316889");
        hit.Title.Should().Be("Buffett Warren E");
    }
}
