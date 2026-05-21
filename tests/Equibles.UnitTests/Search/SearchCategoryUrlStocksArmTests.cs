using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Equibles.UnitTests.Search;

public class SearchCategoryUrlStocksArmTests
{
    [Fact]
    public void CategoryUrl_StocksCategory_RoutesToStocksControllerWithSearchRouteValue()
    {
        // Contract (XML-doc + Stocks switch arm in CategoryUrl): the "See all Stocks
        // for X" link must land on the Stocks landing page with the query plumbed
        // through as the `search` route value, NOT `q` and NOT dropped. Sibling to
        // the existing fallback pin; that test only covers the unmapped categories.
        // A regression renaming the route key (or omitting it) would silently produce
        // an unfiltered /Stocks URL — the user clicks "See all" expecting filtered
        // results and gets the entire universe instead.
        UrlActionContext captured = null;
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Do<UrlActionContext>(ctx => captured = ctx)).Returns("/resolved/path");

        url.CategoryUrl("Stocks", "AAPL");

        captured.Should().NotBeNull();
        captured.Action.Should().Be("Index");
        captured.Controller.Should().Be("Stocks");
        var values = new RouteValueDictionary(captured.Values);
        values.Should().ContainKey("search").WhoseValue.Should().Be("AAPL");
    }
}
