using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FiscalPeriodResolverOutOfRangeFyeDayTests
{
    // Contract (doc-comment + validation gate at the top of Resolve):
    // returns null when the FYE is unknown — and the gate explicitly rejects
    // fyeDay outside 1..31 alongside fyeMonth outside 1..12. The existing
    // `OutOfRangeFyeMonth` pin exercises only the fyeMonth arm; the OR is
    // short-circuit, so the fyeDay > 31 arm is not reached by that test.
    // A nonsensical fyeDay (e.g. a corrupted EntityType.fy field deserialising
    // to 32) must return null so the caller falls back to filing-supplied
    // identity, not throw inside the later DateOnly construction.
    [Fact]
    public void Resolve_OutOfRangeFyeDay_ReturnsNullSoCallerFallsBack()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 09, 30),
            periodEnd: new DateOnly(2024, 09, 28),
            fyeMonth: 9,
            fyeDay: 32
        );

        result.Should().BeNull();
    }
}
