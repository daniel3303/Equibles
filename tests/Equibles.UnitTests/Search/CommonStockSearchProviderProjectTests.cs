using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories.Search;
using Equibles.Search.Abstractions;

namespace Equibles.UnitTests.Search;

/// <summary>
/// Pins the cross-component wiring of the Stocks search group (new global search,
/// #885), 0% covered. For a hit to become a working link, Project must emit
/// Kind "Stock" and the ticker under RouteValues key "ticker" — the exact key
/// SearchCategoryRouteExtensions.HitUrl's "Stock" arm reads. A rename on either
/// side ships a dead link with no compile-time check; this is the only guard.
/// Project is protected → invoked via reflection (the repo's pattern for
/// non-public members; the repo dependency is unused by Project).
/// </summary>
public class CommonStockSearchProviderProjectTests
{
    [Fact]
    public void Project_Stock_EmitsStockKindAndTickerRouteValueForHitUrl()
    {
        var provider = new CommonStockSearchProvider(null);
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };

        var project = typeof(CommonStockSearchProvider).GetMethod(
            "Project",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;

        var hit = (SearchHit)project.Invoke(provider, [stock])!;

        hit.Kind.Should().Be("Stock");
        hit.RouteValues.Should().ContainKey("ticker").WhoseValue.Should().Be("AAPL");
        hit.Title.Should().Be("AAPL");
        hit.Subtitle.Should().Be("Apple Inc.");
    }
}
