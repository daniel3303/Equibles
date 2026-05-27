using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace Equibles.UnitTests.Search;

public class SearchCategoryUrlEconomicIndicatorsArmTests
{
    // Sibling to SearchCategoryUrlStocksArmTests and SearchCategoryUrlFuturesArmTests.
    // CategoryUrl's switch has four arms:
    //   "Stocks"               → Stocks/Index?search=...      (pinned)
    //   "Economic Indicators"  → EconomicData/Index           (THIS pin)
    //   "Futures"              → Cftc/Index                   (pinned)
    //   _                      → Search/Index?q=...&category= (pinned)
    //
    // The Economic Indicators arm is unique among the named arms in two ways:
    //   1. The lookup key is a two-word string with an embedded space — the
    //      only multi-word category key in the switch. A refactor that
    //      "normalises" the key to a single token (e.g. "EconomicIndicators",
    //      "EconomicSeries" to match the SearchHit.Kind constant) would
    //      silently fall through to the search-page fallback. Every "See all
    //      Economic Indicators" link would resolve to /Search?q=...&category=
    //      Economic+Indicators instead of /EconomicData — users would land
    //      on a generic search results page with the FRED time-series chart
    //      preview missing.
    //   2. The arm passes NO route values (unlike "Stocks" which passes
    //      search=query). The XML-doc rationale: there's no FRED-search
    //      input on the EconomicData/Index browse page (it's a curated
    //      indicator catalogue, not a query-driven view). A refactor that
    //      "harmonised" all three named arms to consistently pass
    //      `search = query` would silently inject an unused query
    //      parameter; harmless on the URL itself, but a regression in
    //      the navigation contract that the page's analytics depend on
    //      (clean URL distinguishes "See all" from organic search-driven
    //      navigation).
    //
    // The risk the existing pins (Stocks/Futures/fallback) cannot catch
    // independently:
    //   • A typo in the literal "Economic Indicators" — e.g. "Economic
    //     Indicator" (singular) or "Economic Indicators " (trailing space)
    //     — would compile, pass the Stocks and Futures arm pins (different
    //     keys), pass the fallback pin (its input "Insiders" is a third
    //     unrelated key), and silently route every Economic-Indicators
    //     "See all" link through the search-page fallback. The fallback
    //     pin asserts the substitute returns "/resolved/path" — it does
    //     NOT assert the action/controller is Search, so a regression
    //     where the named arm fell through would slip past unobserved.
    //   • A copy-paste regression that mapped "Economic Indicators" to
    //     the wrong controller (e.g. "Cftc" pasted in from the Futures
    //     arm next to it) is structurally adjacent to the existing arms
    //     and the single most likely accidental swap.
    //
    // The dual assertion (Action == "Index" AND Controller == "EconomicData")
    // pins both the action name AND the controller name. A rename of either
    // surfaces here. No route-values assertion — the contract is "no extra
    // values" and the Stocks/Futures siblings already pin that pattern
    // exhaustively for the value-carrying / value-free distinction.
    [Fact]
    public void CategoryUrl_EconomicIndicatorsGroup_RoutesToEconomicDataIndex()
    {
        UrlActionContext captured = null;
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Do<UrlActionContext>(ctx => captured = ctx)).Returns("/resolved/path");

        url.CategoryUrl("Economic Indicators", "cpi");

        captured.Should().NotBeNull();
        captured.Action.Should().Be("Index");
        captured.Controller.Should().Be("EconomicData");
    }
}
