using System.Reflection;
using Equibles.Cftc.Data.Models;
using Equibles.Cftc.Repositories.Search;
using Equibles.Search.Abstractions;

namespace Equibles.UnitTests.Search;

/// <summary>
/// Pins CftcContractSearchProvider.Project (new global search #885, 0% covered),
/// the last untested provider in the fan-out. For the hit to become a working
/// Futures link, Project must emit Kind "FuturesMarket" and the market code under
/// RouteValues key "marketCode" — the exact key SearchCategoryRouteExtensions.HitUrl's
/// "FuturesMarket" arm reads. Project is protected → reflection (repo pattern).
/// </summary>
public class CftcContractSearchProviderProjectTests
{
    [Fact]
    public void Project_Contract_EmitsFuturesMarketKindAndMarketCodeRouteValue()
    {
        var provider = new CftcContractSearchProvider(null);
        var contract = new CftcContract { MarketCode = "001602", MarketName = "WHEAT-SRW" };

        var project = typeof(CftcContractSearchProvider).GetMethod(
            "Project",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;

        var hit = (SearchHit)project.Invoke(provider, [contract])!;

        hit.Kind.Should().Be("FuturesMarket");
        hit.RouteValues.Should().ContainKey("marketCode").WhoseValue.Should().Be("001602");
        hit.Title.Should().Be("WHEAT-SRW");
        hit.Subtitle.Should().Be("001602");
    }
}
