using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FiscalPeriodResolverEarlyJanuaryNineMonthTests
{
    // Adversarial combination pin. The early-January-FYE normalisation (#3888 /
    // #4423) was proven only on the quarter and annual shapes; this guards the
    // combination it left untested — an early-January filer's nine-month YTD,
    // which travels the quarterly-resolution path (the monthsElapsed <= 10 arm).
    // Johnson & Johnson reports SEC fiscalYearEnd "0103" yet is December-anchored
    // (FY2025 ended 2025-12-28, so FY2026 opens 2025-12-29). Its nine-month YTD
    // ending 2026-09-27 (272 days, the nine-month band) must resolve to FY2026 Q3.
    // Without the normalisation the only FYE on-or-after periodEnd is 2027-01-03,
    // so the period would be labelled FY2027 Q3 — one fiscal year too high, the
    // exact off-by-one #3888 fixed for the quarter shape. A normalisation applied
    // only in the annual/quarter branch (not at the top of Resolve) would regress
    // this case while leaving the existing pins green.
    [Fact]
    public void Resolve_EarlyJanuaryFye_NineMonthYtd_IsDecemberAnchoredQ3()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2025, 12, 29),
            periodEnd: new DateOnly(2026, 9, 27),
            fyeMonth: 1,
            fyeDay: 3
        );

        result.Should().Be((2026, SecFiscalPeriod.Q3));
    }
}
