using Equibles.Search.Abstractions;
using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Equibles.UnitTests.Search;

public class HitUrlStockArmTests
{
    // Contract (SearchCategoryRouteExtensions.HitUrl "Stock" arm):
    //   "Stock" => url.Action(
    //       "Show", "Stocks",
    //       new { ticker = hit.RouteValues.GetValueOrDefault("ticker") })
    //
    // Existing pins:
    //   • SearchCategoryRouteMappingTests: a [Theory] matrix that proves
    //     every shipped Kind ("Stock", "Filing", "EconomicSeries",
    //     "FuturesMarket", "Institution", "Insider", "CongressMember")
    //     resolves to a non-null URL — but it stubs `url.Action(any)` to
    //     return "/resolved/path" for ANY UrlActionContext, so the
    //     specific Action/Controller/RouteValues passed for each arm is
    //     invisible to the assertion.
    //   • CommonStockSearchProviderProjectTests: pins the PROVIDER side —
    //     that `CommonStockSearchProvider.Project` emits Kind="Stock"
    //     with `RouteValues["ticker"]` set. It does NOT exercise HitUrl.
    //
    // The risk this pin uniquely catches:
    //   • A copy-paste regression that swapped the "Stock" arm to a
    //     different action — e.g. `url.Action("Index", "Stocks", ...)`
    //     from a "tidy up — all index pages share Index" cleanup, or
    //     `url.Action("Show", "Profiles", ...)` from a copy-paste edit
    //     of the adjacent "Insider" / "Institution" arms (which both
    //     route to Profiles). Either compiles cleanly, passes the
    //     allowlist matrix test (the substitute returns "/resolved/path"
    //     regardless), passes the provider-side projection pin
    //     (different concern), and silently routes EVERY search-result
    //     stock link in production to either the wrong page on the
    //     correct controller (404 — no Index that takes a ticker) or
    //     to a totally different controller's Show page (Profiles/Show
    //     with the ticker as a person-CIK is a hard 500).
    //
    //   • A regression in the ROUTE-VALUE key — `new { symbol =
    //     hit.RouteValues.GetValueOrDefault("ticker") }` (a "harmonise
    //     with API symbol param" cleanup) — would compile, pass the
    //     existing siblings, and silently strip the ticker from the
    //     resolved URL. StocksController.Show(string ticker) would bind
    //     `ticker = null` and either 404 or render the canonical
    //     no-ticker page.
    //
    //   • A regression in the SOURCE key — reading from a different
    //     RouteValues key than the provider populated (e.g.
    //     `.GetValueOrDefault("symbol")` while provider still writes
    //     "ticker") — would compile, pass the existing siblings, and
    //     ship a dead "ticker = null" link.
    //
    // The single-arm matrix test is structurally a coverage-only pin; it
    // proves "Kind is in the allowlist" without checking "Kind routes to
    // the right place". This pin closes that semantic gap for the
    // highest-volume Kind in the global-search UI (Stock is the dominant
    // result type on every ticker / company-name query).
    //
    // Pin: capture the UrlActionContext passed to IUrlHelper.Action.
    // Assert Action=="Show", Controller=="Stocks", and the ticker route
    // value carries through. Dual on Action/Controller AND on the route
    // value catches the three regressions above. The asserted ticker
    // value uses a real-world ticker ("AAPL") so a swap to a hardcoded
    // string or default value is observable.
    [Fact]
    public void HitUrl_StockKind_RoutesToStocksShowWithTickerRouteValue()
    {
        UrlActionContext captured = null;
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Do<UrlActionContext>(ctx => captured = ctx)).Returns("/resolved/path");
        var hit = new SearchHit
        {
            Kind = "Stock",
            RouteValues = new Dictionary<string, string> { ["ticker"] = "AAPL" },
        };

        url.HitUrl(hit);

        captured.Should().NotBeNull();
        captured.Action.Should().Be("Show");
        captured.Controller.Should().Be("Stocks");
        var values = new RouteValueDictionary(captured.Values);
        values.Should().ContainKey("ticker").WhoseValue.Should().Be("AAPL");
    }
}
