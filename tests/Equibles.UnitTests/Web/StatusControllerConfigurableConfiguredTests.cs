using System.Reflection;
using Equibles.Web.Controllers;
using Equibles.Web.Models;

namespace Equibles.UnitTests.Web;

public class StatusControllerConfigurableConfiguredTests
{
    // Sibling to StatusControllerConfigurableUnconfiguredTests (PR #2295).
    // That pin covers the false arm of the `configured ? configuredReason :
    // unconfiguredReason` ternary; this pin covers the structurally distinct
    // CONFIGURED arm.
    //
    // Contract:
    //   Active = configured
    //   Reason = configured ? configuredReason : unconfiguredReason
    //
    // Why both arms need explicit pins despite the obvious ternary structure:
    //   The unconfigured sibling can be passed AND still hide:
    //   • A "always-active" regression — `Active = true` (unconditional) —
    //     the unconfigured sibling catches this (asserts Active=false on
    //     not-configured input). PASS by the unconfigured sibling.
    //   • A "swap the constants" refactor that only touches the configured
    //     constant string — e.g. someone updates `configuredReason` to a
    //     new banner like "Connected (idle)" but the call sites still pass
    //     "Configured ✓" expecting the old text. This pin asserts the
    //     callsite's literal flows through to Reason verbatim, defending
    //     the input→output identity contract.
    //   • A "consolidate to one reason" refactor — `Reason = configuredReason`
    //     unconditionally — the unconfigured sibling fails (Reason would
    //     be the configured string, not the unconfigured one). But the
    //     INVERSE — `Reason = unconfiguredReason` unconditionally — passes
    //     the unconfigured sibling, fails THIS pin (configured input
    //     would return the unconfigured string). Only the pair catches
    //     both polarities of "consolidate to one reason".
    //
    // The pair (Configured + Unconfigured) defends both ternary branches
    // individually. Any single-arm corruption fails on its corresponding
    // sibling.
    //
    // Pin: invoke with configured=true and distinguishable reason literals.
    // Assert BOTH Active=true AND Reason=configuredReason. Reflection-
    // invoke since Configurable is private static.
    [Fact]
    public void Configurable_Configured_SetsActiveTrueAndRoutesConfiguredReason()
    {
        var method = typeof(StatusController).GetMethod(
            "Configurable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (WorkerStatus)
            method!.Invoke(
                null,
                ["Yahoo", "Daily stock prices", true, "Configured ✓", "API key required"]
            );

        result.Active.Should().BeTrue();
        result.Reason.Should().Be("Configured ✓");
    }
}
