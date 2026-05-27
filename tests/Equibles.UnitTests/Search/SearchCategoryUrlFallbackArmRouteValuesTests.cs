using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Equibles.UnitTests.Search;

public class SearchCategoryUrlFallbackArmRouteValuesTests
{
    // Companion to the named-arm pins (Stocks #1, Economic Indicators #2,
    // Futures #3). The existing SearchCategoryUrlTests fallback test only
    // verifies IUrlHelper returns whatever it returns — it does NOT check
    // which action/controller/route-values the producer passes through.
    //
    // The fallback arm carries FOUR shipped categories ("Insiders",
    // "Institutions", "Congress", "SEC Filings") — categories pinned in
    // the SearchProvider identity family (#2415-#2421). All four route
    // through Search/Index with two distinct route values:
    //
    //   • q        = the user's query string
    //   • category = the original category name (to filter the results)
    //
    // The risks this pin uniquely catches (and the named-arm pins do not):
    //
    //   • Route-key rename — `q` → `query` / `search` (the named Stocks
    //     arm uses `search`!). A maintainer "consolidating" the two
    //     would break /Search filtering — the page would receive no
    //     query and show unfiltered results. Even worse, the symptom
    //     is silent (no 404, no 500).
    //
    //   • Category route-value drop — `category` omitted entirely. The
    //     /Search page would receive `q` but no group filter, so
    //     clicking "See all Insiders" would return mixed-category hits.
    //
    //   • Controller rename — "Search" → "GlobalSearch" / "Find". The
    //     route would resolve to whatever IUrlHelper has registered;
    //     in production this returns a null Action invocation which
    //     renders an empty href.
    //
    // Pin: capture the UrlActionContext, assert Action="Index" +
    // Controller="Search" + both route values keyed correctly.
    [Fact]
    public void CategoryUrl_UnmappedCategory_RoutesToSearchControllerWithQAndCategoryRouteValues()
    {
        UrlActionContext captured = null;
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Do<UrlActionContext>(ctx => captured = ctx)).Returns("/resolved/path");

        url.CategoryUrl("Insiders", "berkshire");

        captured.Should().NotBeNull();
        captured.Action.Should().Be("Index");
        captured.Controller.Should().Be("Search");
        var values = new RouteValueDictionary(captured.Values);
        values.Should().ContainKey("q").WhoseValue.Should().Be("berkshire");
        values.Should().ContainKey("category").WhoseValue.Should().Be("Insiders");
    }
}
