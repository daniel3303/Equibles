using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial sibling to <see cref="FiscalPeriodResolverTests"/>. SEC's
/// submissions <c>fiscalYearEnd</c> field reports a single sticky "MMDD" for a
/// 52/53-week filer whose year-end oscillates around Dec 31 and occasionally
/// lands just into the new year. Johnson &amp; Johnson reports "0103" (Jan 3)
/// even though every recent fiscal year is December-anchored (FY2025 ended
/// 2025-12-28). Treating that as a genuine January year-end labels every period
/// one fiscal year too high — the quarter ending 2026-03-29 resolves to FY2027
/// Q1, and the year ending 2025-12-28 to FY2026 (issue #4423). The resolver
/// must normalise an early-January (year-turn) fiscal-year-end to Dec 31, while
/// leaving a genuine late-January retail filer (Walmart/Target, day ~31)
/// untouched.
/// </summary>
public class FiscalPeriodResolverEarlyJanuaryFiscalYearEndTests
{
    [Fact]
    public void Resolve_EarlyJanuaryFye_Quarter_IsDecemberAnchoredYear()
    {
        // J&J Q1: periodEnd 2026-03-29, SEC fiscalYearEnd "0103". Belongs to
        // the December-anchored fiscal year 2026 (DocumentFiscalYearFocus=2026),
        // not 2027.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2025, 12, 29),
            periodEnd: new DateOnly(2026, 3, 29),
            fyeMonth: 1,
            fyeDay: 3
        );

        result.Should().Be((2026, SecFiscalPeriod.Q1));
    }

    [Fact]
    public void Resolve_EarlyJanuaryFye_Annual_IsDecemberAnchoredYear()
    {
        // J&J FY: periodEnd 2025-12-28, SEC fiscalYearEnd "0103". This is
        // fiscal year 2025 (SEC fy=2025), not 2026.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 12, 30),
            periodEnd: new DateOnly(2025, 12, 28),
            fyeMonth: 1,
            fyeDay: 3
        );

        result.Should().Be((2025, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_GenuineLateJanuaryFye_Quarter_IsUnaffected()
    {
        // Walmart-style retail filer ending Jan 31 is a real January fiscal year
        // labelled by the ending-January calendar year. Its Q1 (Feb–Apr 2024)
        // belongs to FY2025; it must NOT be normalised to December.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 2, 1),
            periodEnd: new DateOnly(2024, 4, 30),
            fyeMonth: 1,
            fyeDay: 31
        );

        result.Should().Be((2025, SecFiscalPeriod.Q1));
    }
}
