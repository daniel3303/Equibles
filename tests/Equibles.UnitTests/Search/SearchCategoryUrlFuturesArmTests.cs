using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace Equibles.UnitTests.Search;

public class SearchCategoryUrlFuturesArmTests
{
    // Pins the "Futures" arm of CategoryUrl to its dedicated browse page (Cftc/Index).
    // The existing SearchCategoryUrlTests only exercises the fallback arm; the
    // per-category arms ("Stocks", "Economic Indicators", "Futures") are each a
    // hard-coded (action, controller) tuple and a rename — e.g. Cftc → Commodities —
    // would silently regress the link target without this assertion.
    [Fact]
    public void CategoryUrl_FuturesGroup_RoutesToCftcIndex()
    {
        UrlActionContext captured = null;
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Do<UrlActionContext>(ctx => captured = ctx)).Returns("/resolved/path");

        url.CategoryUrl("Futures", "wheat");

        captured.Should().NotBeNull();
        captured.Action.Should().Be("Index");
        captured.Controller.Should().Be("Cftc");
    }
}
