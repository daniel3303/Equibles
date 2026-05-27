using Equibles.Search.Abstractions;
using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Equibles.UnitTests.Search;

public class HitUrlInsiderArmTests
{
    // Contract (SearchCategoryRouteExtensions.HitUrl "Insider" arm):
    //   "Insider" => url.Action(
    //       "Insider", "Profiles",
    //       new { ownerCik = hit.RouteValues.GetValueOrDefault("ownerCik") })
    //
    // Sibling to HitUrlStockArmTests. The "Stock" arm hits a distinct
    // (Action, Controller) pair (Show/Stocks). The "Insider" arm is one
    // of THREE arms that all hit the SAME controller "Profiles" with
    // different action names:
    //   • "Institution"   => "Institution", "Profiles", { cik       = ... }
    //   • "Insider"       => "Insider",     "Profiles", { ownerCik  = ... }
    //   • "CongressMember"=> "Member",      "Profiles", { id        = ... }
    //
    // These three arms are structurally adjacent in the source switch,
    // route to the SAME controller, and differ ONLY by action name AND
    // the route-key. A copy-paste edit between them is the single most
    // likely accidental swap. The existing allowlist matrix test stubs
    // `url.Action(any)` to return "/resolved/path" for any
    // UrlActionContext, so any swap among the three Profiles arms is
    // INVISIBLE to it — a regression that mapped
    //   "Insider" => url.Action("Institution", "Profiles", new { cik = ... })
    // would compile cleanly, pass the allowlist matrix (still returns
    // "/resolved/path"), AND pass the InsiderOwnerSearchProviderProject
    // pin (provider-side concern), while silently routing every
    // search-result insider link to the Institution-profile page with
    // an ownerCik value bound to the wrong route parameter.
    //
    // The Insider arm is particularly distinct from the other two
    // Profiles arms by its ROUTE-KEY name: `ownerCik`, not `cik` (the
    // Institution arm's key) and not `id` (the CongressMember arm's
    // key). The route-key name comes from the InsiderOwner domain
    // model — InsiderOwner.OwnerCik is the SEC-issued identifier for
    // a §16 reporting person, distinct from the Institution.Cik (a
    // 13F filer's CIK). A regression that "harmonised" the Insider
    // arm's route key to `cik` (under the false intuition "they're
    // all CIKs anyway") would silently strip the insider identifier
    // from the URL — ProfilesController.Insider(int ownerCik) would
    // bind ownerCik = 0 and render a 404 or the canonical
    // no-such-insider page.
    //
    // The risks this pin uniquely catches and that are unreachable
    // from the existing siblings:
    //
    //   • Action swap among the three Profiles arms — e.g.
    //     `"Insider" => url.Action("Institution", ...)` from a
    //     copy-paste edit of the adjacent line. The allowlist matrix
    //     passes (non-null URL), the provider-side Project pin passes
    //     (different concern), and every search-result insider link
    //     in production routes to the Institution profile page.
    //
    //   • Controller swap — `"Insider" => url.Action("Insider",
    //     "Stocks", ...)` from a refactor that "tidies up" the four
    //     non-Stocks Profiles arms by deleting one. Pin asserts the
    //     exact controller name.
    //
    //   • Route-key drift — `new { cik = ... }` instead of `new {
    //     ownerCik = ... }` from a "consolidate to a single cik
    //     param" cleanup. The allowlist matrix can't see the route
    //     values; the provider Project pin pins the SOURCE side
    //     (provider emits "ownerCik") but doesn't exercise HitUrl's
    //     read of that key. Only an explicit assertion on the
    //     produced route-value key catches this.
    //
    //   • Source-key drift — reading `hit.RouteValues
    //     .GetValueOrDefault("cik")` when the provider populates
    //     "ownerCik" (or vice-versa) — would silently bind null and
    //     ship a dead link. The provider pin and this consumer pin
    //     together close the cross-component wiring.
    //
    // Pin: capture the UrlActionContext, assert Action=="Insider",
    // Controller=="Profiles", and route value "ownerCik" carries the
    // SearchHit's RouteValues["ownerCik"]. The triple assertion
    // catches every swap class above. Use a concrete non-default CIK
    // value (1234567) so a hardcoded-zero or default-value regression
    // surfaces.
    [Fact]
    public void HitUrl_InsiderKind_RoutesToProfilesInsiderWithOwnerCikRouteValue()
    {
        UrlActionContext captured = null;
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Do<UrlActionContext>(ctx => captured = ctx)).Returns("/resolved/path");
        var hit = new SearchHit
        {
            Kind = "Insider",
            RouteValues = new Dictionary<string, string> { ["ownerCik"] = "1234567" },
        };

        url.HitUrl(hit);

        captured.Should().NotBeNull();
        captured.Action.Should().Be("Insider");
        captured.Controller.Should().Be("Profiles");
        var values = new RouteValueDictionary(captured.Values);
        values.Should().ContainKey("ownerCik").WhoseValue.Should().Be("1234567");
    }
}
