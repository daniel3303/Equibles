using Equibles.Search.Abstractions;
using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Equibles.UnitTests.Search;

public class HitUrlFuturesMarketArmTests
{
    // Contract (SearchCategoryRouteExtensions.HitUrl "FuturesMarket" arm):
    //   "FuturesMarket" => url.Action(
    //       "Show", "Cftc",
    //       new { marketCode = hit.RouteValues.GetValueOrDefault("marketCode") })
    //
    // Completes the per-arm HitUrl pin family. The previous five pins
    // cover Stock (#2390), Insider (#2391), CongressMember (#2392),
    // Filing (#2393), Institution (#2394), and EconomicSeries (#2395).
    // FuturesMarket is the final unpinned arm.
    //
    // The FuturesMarket arm has THREE structural traits that the existing
    // sibling pins individually cannot detect:
    //
    //   1. Action "Show" — SHARED with the Stock and EconomicSeries arms.
    //      That's three of the seven arms using the same action name. A
    //      regression that swapped the controller while keeping "Show" —
    //      e.g. `"FuturesMarket" => url.Action("Show", "Stocks", ...)`
    //      from a copy-paste of the adjacent Stock arm — would compile,
    //      pass the allowlist matrix, pass the Stock arm pin (different
    //      Kind input), pass the EconomicSeries pin (different Kind), and
    //      silently route every COT-futures search-result link to the
    //      Stocks controller with marketCode bound as a ticker (404 —
    //      no stock named "067651").
    //
    //   2. Controller "Cftc" is a 4-letter abbreviation — the SHORTEST
    //      controller name in the HitUrl switch. The other arms use
    //      semantic names (Stocks, EconomicData, Profiles). A "spell
    //      out the abbreviation" refactor — `"Cftc"` → `"Commodities"`
    //      or `"FuturesMarkets"` — would compile and ship a 404.
    //
    //   3. Route key "marketCode" is CAMELCASE and DOMAIN-SPECIFIC. The
    //      CFTC publishes futures contracts under 6-char alphanumeric
    //      market codes ("067651" for Crude Oil NYMEX, "088691" for
    //      Gold COMEX). A refactor that "harmonised to a more
    //      readable key" — `marketCode` → `code` or `contractId` —
    //      would silently null-bind the parameter while the matrix
    //      test passes.
    //
    // The risks this pin uniquely catches and that are unreachable
    // from every existing per-arm pin:
    //
    //   • Controller swap with a sibling "Show"-action arm (Stocks
    //     or EconomicData). The pin asserts the exact "Cftc" name.
    //
    //   • Controller-name "spell out" refactor that renames Cftc to a
    //     descriptive name. The pin asserts the exact 4-letter form.
    //
    //   • Route-key drift — `marketCode` → `code` or any other name.
    //     The CftcContractSearchProviderProjectTests pins the source
    //     side (provider emits "marketCode"); this consumer pin
    //     closes the read.
    //
    // Pin: capture the UrlActionContext, assert Action=="Show",
    // Controller=="Cftc", and route value "marketCode" carries the
    // SearchHit's RouteValues["marketCode"]. Use a concrete CFTC
    // market code ("067651" — Light Sweet Crude Oil on NYMEX, the
    // highest-volume futures contract in the entire CFTC COT
    // dataset) so a hardcoded-default regression surfaces.
    [Fact]
    public void HitUrl_FuturesMarketKind_RoutesToCftcShowWithMarketCodeRouteValue()
    {
        UrlActionContext captured = null;
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Do<UrlActionContext>(ctx => captured = ctx)).Returns("/resolved/path");
        var hit = new SearchHit
        {
            Kind = "FuturesMarket",
            RouteValues = new Dictionary<string, string> { ["marketCode"] = "067651" },
        };

        url.HitUrl(hit);

        captured.Should().NotBeNull();
        captured.Action.Should().Be("Show");
        captured.Controller.Should().Be("Cftc");
        var values = new RouteValueDictionary(captured.Values);
        values.Should().ContainKey("marketCode").WhoseValue.Should().Be("067651");
    }
}
