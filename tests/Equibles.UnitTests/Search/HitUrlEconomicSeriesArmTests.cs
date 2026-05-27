using Equibles.Search.Abstractions;
using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Equibles.UnitTests.Search;

public class HitUrlEconomicSeriesArmTests
{
    // Contract (SearchCategoryRouteExtensions.HitUrl "EconomicSeries" arm):
    //   "EconomicSeries" => url.Action(
    //       "Show", "EconomicData",
    //       new { seriesId = hit.RouteValues.GetValueOrDefault("seriesId") })
    //
    // Fifth in the per-arm HitUrl pin family
    // (Stock #2390, Insider #2391, CongressMember #2392, Filing #2393,
    // Institution #2394). EconomicSeries is structurally distinctive in
    // two ways neither the existing per-arm pins nor the allowlist
    // matrix can detect:
    //
    //   1. Action "Show" is SHARED with the Stock arm — but the
    //      Controller "EconomicData" is unique. The Stock arm uses
    //      "Stocks". A regression that pasted from the adjacent
    //      Stock arm (the two share an action name, making the
    //      one-line copy-paste easy) and only changed the action while
    //      leaving the controller as "Stocks" — e.g.
    //      `"EconomicSeries" => url.Action("Show", "Stocks", new {
    //      seriesId = ... })` — would compile, pass the Stock arm pin
    //      (asserts on Kind="Stock", not "EconomicSeries"), pass the
    //      allowlist matrix (returns "/resolved/path"), and silently
    //      route every FRED series search-result link to the
    //      StocksController.Show(string ticker) action with the
    //      seriesId bound as a ticker — a 404 because no stock with
    //      that name exists.
    //
    //   2. Route key "seriesId" is camelCase — distinct from every
    //      other arm's lowercase keys (ticker, cik, ownerCik, id,
    //      marketCode). The FRED upstream emits series IDs like
    //      "CPIAUCSL" or "UNRATE"; the camelCase route-key matches
    //      EconomicDataController.Show(string seriesId)'s parameter
    //      name verbatim. A "tidy up route-key casing" refactor that
    //      lowered to "seriesid" or harmonised to a different name
    //      ("id", "series") would silently null-bind the parameter.
    //
    // The risks this pin uniquely catches:
    //
    //   • Controller swap with the Stock arm — paste of `"Stocks"`
    //     when copying from the adjacent Stock case (both use action
    //     "Show"). The allowlist matrix can't see this, and the Stock
    //     arm pin doesn't test EconomicSeries input.
    //
    //   • Controller drift to a different existing controller —
    //     `"EconomicData"` → `"Macro"` from a future "rename to
    //     macro indicators" refactor. The pin asserts the exact
    //     "EconomicData" name.
    //
    //   • Route-key drift — "seriesId" → "id" (matches the
    //     CongressMember arm) or "seriesId" → "series" (cleanup).
    //     The FredSeriesSearchProvider Project pin pins the source
    //     side (provider emits "seriesId"); this consumer pin closes
    //     the read.
    //
    // Pin: capture the UrlActionContext, assert Action=="Show"
    // (matches the Stock arm — proves a Kind-correct read), Controller
    // =="EconomicData" (distinguishes from "Stocks"), and route value
    // "seriesId" carries the SearchHit's RouteValues["seriesId"]. Use
    // a concrete FRED series ID ("CPIAUCSL" — the Consumer Price Index
    // for All Urban Consumers, the most-referenced FRED series) so a
    // hardcoded-default regression surfaces.
    [Fact]
    public void HitUrl_EconomicSeriesKind_RoutesToEconomicDataShowWithSeriesIdRouteValue()
    {
        UrlActionContext captured = null;
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Do<UrlActionContext>(ctx => captured = ctx)).Returns("/resolved/path");
        var hit = new SearchHit
        {
            Kind = "EconomicSeries",
            RouteValues = new Dictionary<string, string> { ["seriesId"] = "CPIAUCSL" },
        };

        url.HitUrl(hit);

        captured.Should().NotBeNull();
        captured.Action.Should().Be("Show");
        captured.Controller.Should().Be("EconomicData");
        var values = new RouteValueDictionary(captured.Values);
        values.Should().ContainKey("seriesId").WhoseValue.Should().Be("CPIAUCSL");
    }
}
