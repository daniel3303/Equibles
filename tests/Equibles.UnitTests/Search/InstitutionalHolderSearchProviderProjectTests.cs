using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Search;
using Equibles.Search.Abstractions;

namespace Equibles.UnitTests.Search;

/// <summary>
/// Pins InstitutionalHolderSearchProvider.Project (new global search #885, 0%
/// covered). The Subtitle joins city/state but must drop null/whitespace parts —
/// a naive "$"{City}, {State}"" would emit ", " for a holder with no location.
/// Also pins the cross-component wiring HitUrl's "Institution" arm relies on
/// (Kind + RouteValues["cik"]). Project is protected → reflection (repo pattern).
/// </summary>
public class InstitutionalHolderSearchProviderProjectTests
{
    [Fact]
    public void Project_NullAndWhitespaceLocationParts_SubtitleEmptyAndWiresInstitutionRoute()
    {
        var provider = new InstitutionalHolderSearchProvider(null);
        var holder = new InstitutionalHolder
        {
            Cik = "0001067983",
            Name = "Berkshire Hathaway Inc",
            City = null,
            StateOrCountry = "   ",
        };

        var project = typeof(InstitutionalHolderSearchProvider).GetMethod(
            "Project",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;

        var hit = (SearchHit)project.Invoke(provider, [holder])!;

        hit.Subtitle.Should().BeEmpty();
        hit.Kind.Should().Be("Institution");
        hit.RouteValues.Should().ContainKey("cik").WhoseValue.Should().Be("0001067983");
        hit.Title.Should().Be("Berkshire Hathaway Inc");
    }
}
