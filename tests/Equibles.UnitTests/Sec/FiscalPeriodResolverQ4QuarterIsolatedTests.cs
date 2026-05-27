using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FiscalPeriodResolverQ4QuarterIsolatedTests
{
    // Contract (XML-doc summary): "Derives a fact's (FiscalYear, FiscalPeriod)
    // from the period it actually measures." The period switch at lines 102-108
    // resolves a quarter/half/nine-month duration to one of Q1/Q2/Q3/Q4 based
    // on how many months into the fiscal year the period closes:
    //   monthsElapsed switch { <= 4 => Q1, <= 7 => Q2, <= 10 => Q3, _ => Q4 }.
    //
    // Existing sibling pins cover the Q1, Q2 (HalfYear), and Q3 (NineMonth)
    // arms. The default arm `_ => Q4` — which fires when the period closes
    // 11+ months into the fiscal year — has no pin. The catch-all is the
    // load-bearing case for isolated Q4 quarterly periods reported in
    // 10-K filings: 10-Q is filed for Q1-Q3, and the 10-K both reports
    // the FY total AND publishes the standalone Q4 quarter for
    // comparability. Without this pin a refactor that:
    //   • Inverts the switch ordering (e.g. `_ => Q1` from a copy-paste
    //     that swapped the default with the first arm) would compile,
    //     pass all three existing arm pins, and silently misclassify
    //     every standalone Q4 fact as Q1.
    //   • Replaces the default with one of the explicit arms (`<= 11 =>
    //     Q4` and removing the default) would crash at runtime on
    //     monthsElapsed values that don't fit any arm — but only on
    //     filings that trip the new gap, which test fixtures rarely
    //     cover.
    //   • "Simplifies" the entire switch to `monthsElapsed / 3 + 1`
    //     would compile, look reasonable, and silently break the
    //     boundary behaviour at every arm cutoff (this pin would
    //     catch the Q4 cutoff specifically).
    //
    // Pick an Oct-31 FYE filer (fiscalYearStart = Nov 1) reporting a
    // standalone Q4 quarter Aug 1 - Oct 31. That's 91 days (quarter
    // bucket, 80-100), and monthsElapsed = (2024-2023)*12 + (10-11) = 11
    // → default arm → Q4. The endingFye lookup picks Oct 31 of the same
    // calendar year (the smallest candidate >= periodEnd), so the
    // returned fiscal year is 2024.
    //
    // Pin: Resolve(2024-08-01, 2024-10-31, fyeMonth=10, fyeDay=31)
    // → (2024, Q4). The exact tuple equality catches any swap-with-
    // another-arm regression and any year-resolution shift.
    [Fact]
    public void Resolve_QuarterCloseAtNonCalendarFyeWithElevenMonthsElapsed_ReturnsQ4()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 8, 1),
            periodEnd: new DateOnly(2024, 10, 31),
            fyeMonth: 10,
            fyeDay: 31
        );

        result.Should().Be((2024, SecFiscalPeriod.Q4));
    }
}
