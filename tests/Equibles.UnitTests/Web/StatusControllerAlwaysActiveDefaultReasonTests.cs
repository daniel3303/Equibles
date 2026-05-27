using System.Reflection;
using Equibles.Web.Controllers;
using Equibles.Web.Models;

namespace Equibles.UnitTests.Web;

public class StatusControllerAlwaysActiveDefaultReasonTests
{
    // Sibling to StatusControllerConfigurable{Configured,Unconfigured}Tests
    // (PRs #2295 and #2296). Those defend the API-key-gated worker builder;
    // this pin covers the structurally distinct ALWAYS-ACTIVE worker builder
    // that the /Status dashboard uses for workers that have no key requirement
    // (SEC EDGAR, House and Senate disclosures, CFTC public CSVs, CBOE feeds —
    // anything served from a public CDN). The contract is fixed:
    //   Active = true (unconditional)
    //   Reason = the supplied reason, or "Always active — no API key required"
    //            when the caller omits the argument
    //
    // The risk this pin uniquely catches:
    //   • DEFAULT-CHANGE regression — the default-parameter literal
    //     "Always active — no API key required" is the exact string the
    //     /Status dashboard renders below every always-on worker. A
    //     refactor that "tidied" the wording (e.g. to "No key needed",
    //     "Always running", a localised resource key) would compile,
    //     pass every other test (callers that supply an explicit reason
    //     wouldn't notice), and silently change the dashboard's
    //     always-on workers from the operator-recognisable "Always
    //     active — no API key required" banner to something new. The
    //     em-dash (U+2014) wrapped in single spaces is part of the
    //     exact-string contract — an ASCII-hyphen replacement would
    //     also visibly change every dashboard row.
    //   • DROP-the-default regression — making `reason` non-optional
    //     would force every call site to pass it. The compiler catches
    //     non-passing call sites, but the regression risk is that a
    //     refactor adds a NEW default like "" (empty) and every
    //     no-arg call site silently renders an empty Reason cell.
    //   • ACTIVE-FLIP regression — `Active = false` (consolidate with
    //     Configurable) would render every always-on worker as
    //     unconfigured/red on the /Status dashboard. The complementary
    //     Configurable pins can't see this — those use the Configurable
    //     helper, not AlwaysActive.
    //
    // Pin: invoke with name+description and OMIT the reason argument
    // (so the default-parameter literal is exercised). Assert BOTH
    // Active=true AND the exact default literal string. The em-dash
    // encoding is asserted by exact equality. Reflection-invoke since
    // AlwaysActive is private static.
    //
    // Note on invocation: reflection's Invoke does NOT apply default
    // parameter values automatically — we must pass Type.Missing for
    // omitted optional parameters to trigger default-value substitution.
    // (Confirmed via the standard .NET Reflection contract: only when
    // an array element equals Type.Missing does the runtime fall back
    // to the method's declared default; otherwise it would pass null
    // as the actual argument, bypassing the default literal entirely.)
    [Fact]
    public void AlwaysActive_ReasonOmitted_ReturnsDefaultLiteralAndActiveTrue()
    {
        var method = typeof(StatusController).GetMethod(
            "AlwaysActive",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (WorkerStatus)
            method!.Invoke(null, ["SEC EDGAR", "Filings + facts + insider trading", Type.Missing]);

        result.Active.Should().BeTrue();
        result.Reason.Should().Be("Always active — no API key required");
    }
}
