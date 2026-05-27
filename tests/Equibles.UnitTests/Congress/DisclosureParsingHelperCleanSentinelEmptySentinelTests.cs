using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperCleanSentinelEmptySentinelTests
{
    // CleanSentinel is the unpinned cell-cleanup helper invoked on
    // every congressional-disclosure HTML cell after parsing. Its
    // body:
    //     string.IsNullOrEmpty(value) || value == EmptySentinel
    //         ? null : value
    // where `EmptySentinel = "--"`. The contract has THREE distinct
    // null-returning arms: null input, empty string, and the exact
    // sentinel literal "--". Anything else passes through.
    //
    // The "--" sentinel is the congressional Periodic Transaction
    // Report (PTR) HTML form's convention for "field intentionally
    // left blank" — used when a representative reports a
    // transaction without a specific asset description or pricing
    // metadata. The downstream pipeline relies on null to mean
    // "missing"; a stray "--" string passing through would render
    // in the dashboard as a literal "--" in the asset/amount column
    // and confuse analysts.
    //
    // The risks this pin uniquely catches:
    //
    //   • Dropped sentinel arm — `string.IsNullOrEmpty(value) ? null
    //     : value` (under "EmptySentinel is dead code, no one passes
    //     '--' anymore") would compile, pass any test that uses
    //     null/empty input, and silently return "--" verbatim for
    //     every blanked PTR field — visible as literal "--" cells in
    //     the congressional-trades dashboard.
    //
    //   • Wrong-sentinel-constant regression — `EmptySentinel = "-"`
    //     or `"–"` (en-dash) or `"---"` (triple) — the constant value
    //     is the most edit-tempting target. Each such regression
    //     leaves the existing IsNullOrEmpty arm working but breaks
    //     this specific arm. Pinning the EXACT "--" literal catches
    //     all of these.
    //
    //   • Over-broad match — `value?.Trim() == EmptySentinel` (a
    //     "tolerance" refactor) would convert " -- " inputs that
    //     legitimately mean a damaged cell into null too. This pin
    //     only defends the canonical exact-match shape; an over-broad
    //     refactor would PASS this pin and fail a follow-up
    //     " -- " → "-- " sibling pin.
    //
    // Adversarial input: "--" (the exact sentinel literal). Dual
    // assertion: result is null AND the original input was non-null
    // — so we know the null is from the sentinel arm, not from
    // accidental null propagation.
    [Fact]
    public void CleanSentinel_ExactEmptySentinelLiteral_ReturnsNullViaSentinelArm()
    {
        var input = "--";

        var result = DisclosureParsingHelper.CleanSentinel(input);

        input.Should().NotBeNull();
        result.Should().BeNull("the '--' literal is the PTR's empty-cell sentinel");
    }
}
