using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FiscalPeriodResolverOutOfRangeFyeMonthHighTests
{
    [Fact]
    public void Resolve_FyeMonthAboveTwelve_ReturnsNullSoCallerFallsBack()
    {
        // Validation gate (FiscalPeriodResolver.cs:45): `fyeMonth < 1 || fyeMonth > 12
        // || fyeDay < 1 || fyeDay > 31 → return null`. The four-arm OR is short-circuit,
        // so each sibling needs its own pin to ensure the next refactor that, say,
        // collapses to `fyeMonth is < 1 or > 12` doesn't accidentally lose one boundary.
        // Existing pins cover fyeMonth = 0, fyeDay = 0, and fyeDay = 32 — this closes
        // the fyeMonth > 12 sibling (e.g. a corrupted EntityType.fy value deserialising
        // to 13). Without the guard the value would feed `DateTime.DaysInMonth(year, 13)`
        // in CreateSafe and throw ArgumentOutOfRangeException, escalating a data-quality
        // glitch into a worker-crashing exception instead of the contractual null fallback.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 09, 30),
            periodEnd: new DateOnly(2024, 09, 28),
            fyeMonth: 13,
            fyeDay: 30
        );

        result.Should().BeNull();
    }
}
