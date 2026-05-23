using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Contract: Resolve returns a (Year, Period) tuple or null — it never throws
/// for valid DateOnly inputs with a valid FYE month/day. Internally it builds
/// candidates at periodEnd.Year ± 1; when periodEnd.Year is 9999 the +1
/// candidate overflows DateOnly's valid range (year 10000) and
/// DateTime.DaysInMonth throws ArgumentOutOfRangeException.
/// </summary>
public class FiscalPeriodResolverYearOverflowTests
{
    [Fact(Skip = "GH-1876 — CreateSafe overflows to year 10000")]
    public void Resolve_PeriodEndInYear9999_DoesNotThrow()
    {
        // A quarterly period ending Dec 31 9999 with a December FYE triggers
        // CreateSafe(10000, 12, 31) via the periodEnd.Year + 1 candidate.
        var periodStart = new DateOnly(9999, 10, 1);
        var periodEnd = new DateOnly(9999, 12, 31);

        var act = () => FiscalPeriodResolver.Resolve(periodStart, periodEnd, 12, 31);

        act.Should()
            .NotThrow(
                "Resolve should handle dates at DateOnly.MaxValue gracefully instead of overflowing to year 10000"
            );
    }
}
