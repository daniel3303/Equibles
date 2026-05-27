using Equibles.Search.Abstractions;
using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Equibles.UnitTests.Search;

public class HitUrlInstitutionArmTests
{
    // Contract (SearchCategoryRouteExtensions.HitUrl "Institution" arm):
    //   "Institution" => url.Action(
    //       "Institution", "Profiles",
    //       new { cik = hit.RouteValues.GetValueOrDefault("cik") })
    //
    // Completes the Profiles-arm trio (Institution/Insider/CongressMember)
    // pin family started by #2391 (Insider) and #2392 (CongressMember).
    // All three Profiles arms share the same controller and differ only
    // by action name AND the route-key:
    //   • "Institution"    => "Institution", "Profiles", { cik       = ... } ← THIS pin
    //   • "Insider"        => "Insider",     "Profiles", { ownerCik  = ... } ← pinned
    //   • "CongressMember" => "Member",      "Profiles", { id        = ... } ← pinned
    //
    // The Institution arm uses the BARE "cik" key — the unqualified form
    // that "looks like" the natural CIK identifier across the whole SEC
    // domain. The Insider arm distinguishes its key with the "owner"
    // prefix (`ownerCik`) precisely because both Insiders AND
    // Institutions have CIKs, and the prefix prevents cross-talk. A
    // refactor that "harmonised" both arms to the bare `cik` key
    // — under the false intuition that "CIK is CIK, the namespace is
    // implicit from the Kind" — would silently couple the two arms'
    // route-key parsing. The Insider arm pin catches the Insider side
    // of the harmonisation; this pin catches the Institution side.
    //
    // The risks this pin uniquely catches and that are unreachable from
    // every existing sibling (allowlist matrix, provider-side projection
    // pin, Stock/Insider/CongressMember/Filing arm pins):
    //
    //   • Action swap with sibling Profiles arms — e.g.
    //     `"Institution" => url.Action("Insider", "Profiles", ...)` from
    //     a copy-paste of the adjacent Insider arm. The Insider arm pin
    //     still passes (different Kind input); the allowlist matrix
    //     still passes; every Institution search result silently
    //     routes to the Insider profile page.
    //
    //   • Action-name "harmonisation" — `"Institution"` → `"Show"`
    //     under a "use the generic show action everywhere" cleanup. The
    //     pin asserts the exact "Institution" action so the
    //     specialised action survives.
    //
    //   • Route-key collapse — `cik` → `ownerCik` from a copy-paste of
    //     the Insider arm. The InstitutionalHolderSearchProvider Project
    //     pin pins the source side (provider emits "cik"); this consumer
    //     pin closes the read.
    //
    //   • Wrong-controller drift — `url.Action("Institution",
    //     "Institutions", ...)` (plural — there's no Institutions
    //     controller; institutions live in Profiles). A "split off
    //     institutions" refactor would compile and ship a 404.
    //
    // Pin: capture the UrlActionContext, assert Action=="Institution",
    // Controller=="Profiles", and the route value "cik" carries the
    // SearchHit's RouteValues["cik"]. Use a concrete non-default CIK
    // value ("0001067983" — Berkshire Hathaway's actual SEC CIK, the
    // canonical "largest 13F filer" example) so a hardcoded-default
    // regression surfaces.
    [Fact]
    public void HitUrl_InstitutionKind_RoutesToProfilesInstitutionWithCikRouteValue()
    {
        UrlActionContext captured = null;
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Do<UrlActionContext>(ctx => captured = ctx)).Returns("/resolved/path");
        var hit = new SearchHit
        {
            Kind = "Institution",
            RouteValues = new Dictionary<string, string> { ["cik"] = "0001067983" },
        };

        url.HitUrl(hit);

        captured.Should().NotBeNull();
        captured.Action.Should().Be("Institution");
        captured.Controller.Should().Be("Profiles");
        var values = new RouteValueDictionary(captured.Values);
        values.Should().ContainKey("cik").WhoseValue.Should().Be("0001067983");
    }
}
