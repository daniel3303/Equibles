using System.Reflection;
using Equibles.Web.Controllers;
using Equibles.Web.Models;

namespace Equibles.UnitTests.Web;

public class StatusControllerConfigurableUnconfiguredTests
{
    // StatusController.Configurable is the per-worker builder that powers the
    // /Status dashboard's Workers panel. Every API-key-gated worker (FRED,
    // FINRA, OpenAI embeddings, etc.) calls it with `configured: hasKey`,
    // and the dashboard surfaces:
    //   • Active flag — drives the green/grey badge on the panel
    //   • Reason — shows EITHER the success message OR the "API key needed"
    //     hint (which is what an operator clicks through to fix)
    //
    // Contract:
    //   Active = configured
    //   Reason = configured ? configuredReason : unconfiguredReason
    //
    // The risk this pin uniquely catches: Configurable is private static
    // and currently untested. A "tidy the ternary" refactor that
    // swapped the two reasons —
    //   Reason = configured ? unconfiguredReason : configuredReason
    // — would compile, render an inactive worker with a "Configured ✓"
    // message (the operator sees green-text-on-grey-badge and assumes
    // the worker is just paused, when actually the API key is missing).
    // The complementary regression: `Active = !configured` (logic
    // flip) would render an UNCONFIGURED worker as active. Either way,
    // the dashboard becomes a silent liar.
    //
    // Pick the unconfigured arm specifically — its assertions distinguish
    // both regression classes at once:
    //   • Working contract: Active=false AND Reason=unconfiguredReason.
    //   • Active-flip regression: Active=true (fails the .BeFalse()).
    //   • Reason-swap regression: Reason=configuredReason (fails the
    //     equality check on the literal "API key required").
    //   • Drop-the-ternary (always configuredReason): Reason=configured
    //     (fails the literal check).
    //
    // The configured arm would also catch these, but the unconfigured
    // path is the production-critical one — that's the path operators
    // see at first-time setup and after deployment-rotation key churn.
    // A misrouted reason there masks real outages with a happy message.
    //
    // Reflection-invoke since the helper is private static. Construct
    // with deliberately distinguishable reason strings so the assertion
    // can verify the routing, not just the structure.
    [Fact]
    public void Configurable_NotConfigured_SetsActiveFalseAndRoutesUnconfiguredReason()
    {
        var method = typeof(StatusController).GetMethod(
            "Configurable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (WorkerStatus)
            method!.Invoke(
                null,
                ["Yahoo", "Daily stock prices", false, "Configured ✓", "API key required"]
            );

        result.Active.Should().BeFalse();
        result.Reason.Should().Be("API key required");
    }
}
