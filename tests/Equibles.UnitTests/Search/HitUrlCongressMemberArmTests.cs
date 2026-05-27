using Equibles.Search.Abstractions;
using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Equibles.UnitTests.Search;

public class HitUrlCongressMemberArmTests
{
    // Contract (SearchCategoryRouteExtensions.HitUrl "CongressMember" arm):
    //   "CongressMember" => url.Action(
    //       "Member", "Profiles",
    //       new { id = hit.RouteValues.GetValueOrDefault("id") })
    //
    // Completes the Profiles-arm family pin set. The three Profiles arms
    // share the same controller and differ only by action name and the
    // route-key:
    //   • "Institution"    => "Institution", "Profiles", { cik       = ... }
    //   • "Insider"        => "Insider",     "Profiles", { ownerCik  = ... } ← pinned
    //   • "CongressMember" => "Member",      "Profiles", { id        = ... } ← THIS pin
    //
    // CongressMember is uniquely vulnerable in the family in two ways:
    //
    //   1. The action name is "Member" — DOES NOT match the Kind string
    //      "CongressMember". The Insider/Institution arms have a more
    //      direct Kind ↔ action correspondence (Kind "Insider" → action
    //      "Insider", Kind "Institution" → action "Institution"). A
    //      refactor that "harmonised" the asymmetric arm to
    //      `"CongressMember" => url.Action("CongressMember", "Profiles",
    //      ...)` would compile, pass the allowlist matrix (still returns
    //      "/resolved/path"), pass the CongressMemberSearchProvider
    //      projection pin (provider-side concern), and silently route
    //      every congress-member search-result link to a 404
    //      (ProfilesController.CongressMember doesn't exist — only
    //      Member does).
    //
    //   2. The route-key "id" is the most generic name in the entire
    //      HitUrl switch. The other arms all use semantically distinct
    //      keys (ticker, cik, ownerCik, seriesId, marketCode). A
    //      refactor that "consolidated to a uniform memberId" cleanup,
    //      or a copy-paste from an adjacent arm that pulled in a
    //      different key like `cik` or `id` reading from the wrong
    //      source, would silently bind id=0. CongressMember IDs come
    //      from the upstream Congress.gov API (not a CIK), so a
    //      cross-talk regression between this arm's "id" and the
    //      Institution arm's "cik" would bind a number that looks
    //      plausible but routes to no actual member.
    //
    // The risks this pin uniquely catches and that are unreachable
    // from every existing sibling (allowlist matrix + provider pin +
    // Stock/Insider arm pins):
    //
    //   • Action swap with sibling arms — e.g.
    //     `"CongressMember" => url.Action("Insider", "Profiles", ...)`
    //     from a copy-paste edit of the adjacent Insider arm. The
    //     allowlist matrix passes (still returns "/resolved/path"),
    //     the Insider arm pin still asserts on its own input (Kind
    //     "Insider"), and every CongressMember search result silently
    //     routes to the Insider profile page.
    //
    //   • Action-name "harmonisation" — `"Member"` → `"CongressMember"`
    //     under the false intuition that "the action should match the
    //     Kind for consistency". This is the SPECIFIC asymmetry in the
    //     switch that's unique to this arm — neither the Insider nor
    //     Institution arm pin can detect it because their Kind/action
    //     happen to already match.
    //
    //   • Route-key drift — `id` → `memberId` or `id` → `cik` from a
    //     copy-paste of an adjacent arm. The provider Project pin
    //     pins the source side (CongressMember provider emits "id");
    //     this consumer pin closes the read.
    //
    // Pin: capture the UrlActionContext, assert Action=="Member"
    // (NOT "CongressMember"), Controller=="Profiles", and the route
    // value "id" carries the SearchHit's RouteValues["id"]. The
    // assertion on "Member" specifically (NOT the Kind name) catches
    // the Kind/action harmonisation regression. Use a concrete
    // non-default id ("4242") so a hardcoded-zero or default-value
    // regression surfaces.
    [Fact]
    public void HitUrl_CongressMemberKind_RoutesToProfilesMemberWithIdRouteValue()
    {
        UrlActionContext captured = null;
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Do<UrlActionContext>(ctx => captured = ctx)).Returns("/resolved/path");
        var hit = new SearchHit
        {
            Kind = "CongressMember",
            RouteValues = new Dictionary<string, string> { ["id"] = "4242" },
        };

        url.HitUrl(hit);

        captured.Should().NotBeNull();
        captured.Action.Should().Be("Member");
        captured.Controller.Should().Be("Profiles");
        var values = new RouteValueDictionary(captured.Values);
        values.Should().ContainKey("id").WhoseValue.Should().Be("4242");
    }
}
