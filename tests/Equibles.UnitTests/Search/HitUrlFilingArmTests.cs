using Equibles.Search.Abstractions;
using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Equibles.UnitTests.Search;

public class HitUrlFilingArmTests
{
    // Contract (SearchCategoryRouteExtensions.HitUrl "Filing" arm):
    //   "Filing" => url.Action(
    //       "ShowDocument", "Stocks",
    //       new
    //       {
    //           ticker = hit.RouteValues.GetValueOrDefault("ticker"),
    //           id     = hit.RouteValues.GetValueOrDefault("id"),
    //       })
    //
    // Sibling to the Stock (#2390), Insider (#2391), and CongressMember
    // (#2392) HitUrl arm pins. The Filing arm is uniquely vulnerable in
    // two structural ways NONE of the other arms shares:
    //
    //   1. SAME CONTROLLER as the Stock arm ("Stocks"), DIFFERENT
    //      ACTION ("ShowDocument" vs "Show"). The other arms either
    //      have distinct controllers or live in the Profiles family.
    //      A regression that swapped `"ShowDocument"` to `"Show"` —
    //      e.g. a "tidy up redundant action prefix" cleanup — would
    //      compile, pass the allowlist matrix (still returns
    //      "/resolved/path"), pass the Stock arm pin (which asserts
    //      on a Stock-Kind input, not Filing), AND silently route
    //      every search-result filing link to the per-stock detail
    //      page with both `ticker` AND `id` bound as route values.
    //      StocksController.Show(string ticker) would ignore the `id`
    //      and render the stock page — the user clicked "ShowDocument
    //      'AAPL 10-K'" expecting the filing reader, got the stock
    //      home page instead.
    //
    //   2. TWO route-value keys (ticker AND id) — the ONLY HitUrl arm
    //      that emits more than one route value. A regression that
    //      dropped one of the two `hit.RouteValues.GetValueOrDefault`
    //      calls (e.g. `new { id = hit.RouteValues.GetValueOrDefault
    //      ("id") }` from a "we don't need the ticker since the id
    //      is unique anyway" cleanup) would silently null-bind the
    //      ticker. StocksController.ShowDocument(string ticker, Guid
    //      id) needs both — dropping ticker yields a 404 because the
    //      route template is `/Stocks/{ticker}/ShowDocument/{id}`.
    //      The single-route-value siblings can't catch this — they
    //      all emit exactly one route value, so a "drop one" refactor
    //      that touches Filing slips past their assertions.
    //
    // The risks this pin uniquely catches and that are unreachable
    // from every existing HitUrl arm pin:
    //
    //   • Action-name swap with the Stock arm — `"Show" instead of
    //     "ShowDocument"`. Stock arm pin still passes (different Kind);
    //     allowlist matrix passes; the swap is silent.
    //
    //   • Action-name swap with sibling arms — `"Insider"` /
    //     `"Member"` / `"Institution"` accidentally pasted from the
    //     Profiles family. The Profiles-arm pins each assert their
    //     OWN Kind input; none distinguishes "ShowDocument" from
    //     their action name.
    //
    //   • Drop of EITHER route value. Both must be present for the
    //     route to resolve. Asserting both keys carry their respective
    //     SearchHit values catches a one-of-two drop independently.
    //
    //   • Source-key drift — reading `documentId` when the provider
    //     populates `id`, or `symbol` instead of `ticker`. Both keys
    //     must read from their documented source name.
    //
    //   • Controller swap — `"Documents"` instead of `"Stocks"`. The
    //     Filing arm's controller match with the Stock arm is non-
    //     obvious (filings ARE on the Stocks controller because each
    //     filing belongs to one ticker), so a "move filings to their
    //     own controller" refactor would compile and pass every
    //     other arm pin.
    //
    // Pin: capture the UrlActionContext, assert Action=="ShowDocument"
    // (NOT "Show"), Controller=="Stocks", and BOTH route values
    // (ticker and id) carry their SearchHit values. Use distinct
    // concrete values for each (ticker="AAPL", id="0000320193-23-
    // 000106") so a swap between the two keys is observable.
    [Fact]
    public void HitUrl_FilingKind_RoutesToStocksShowDocumentWithTickerAndIdRouteValues()
    {
        UrlActionContext captured = null;
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Do<UrlActionContext>(ctx => captured = ctx)).Returns("/resolved/path");
        var hit = new SearchHit
        {
            Kind = "Filing",
            RouteValues = new Dictionary<string, string>
            {
                ["ticker"] = "AAPL",
                ["id"] = "0000320193-23-000106",
            },
        };

        url.HitUrl(hit);

        captured.Should().NotBeNull();
        captured.Action.Should().Be("ShowDocument");
        captured.Controller.Should().Be("Stocks");
        var values = new RouteValueDictionary(captured.Values);
        values.Should().ContainKey("ticker").WhoseValue.Should().Be("AAPL");
        values.Should().ContainKey("id").WhoseValue.Should().Be("0000320193-23-000106");
    }
}
