using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Contract: an instant fact (zero-duration, e.g. a balance-sheet value) only
/// resolves to FullYear when its date sits within the ±14-day FYE window. A Q1
/// balance-sheet instant dated at a quarter-end is ~91 days off the December FYE,
/// so Resolve must return null and let the caller fall back to filing identity —
/// resolving it to FullYear would collapse distinct quarter-end balance sheets
/// onto one row, the exact defect this class exists to prevent (#982). Existing
/// tests pin the at-FYE instant (FullYear) and the too-far ANNUAL path, but not
/// the too-far INSTANT path.
/// </summary>
public class FiscalPeriodResolverInstantNotAtFyeTests
{
    [Fact]
    public void Resolve_InstantAtQuarterEndFarFromDecemberFye_ReturnsNull()
    {
        // Instant ⇒ periodStart == periodEnd. March 31 is ~91 days from a Dec 31
        // FYE, well outside the ±14-day window.
        var instant = new DateOnly(2024, 3, 31);

        var result = FiscalPeriodResolver.Resolve(instant, instant, 12, 31);

        result
            .Should()
            .BeNull(
                "a quarter-end instant is too far from the FYE to resolve; the caller must fall back"
            );
    }
}
