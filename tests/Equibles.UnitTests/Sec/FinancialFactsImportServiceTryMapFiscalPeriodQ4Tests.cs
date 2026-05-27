using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryMapFiscalPeriodQ4Tests
{
    // Sibling to FinancialFactsImportServiceTryMapFiscalPeriodFyTests (FY
    // arm) and FinancialFactsImportServiceTryMapFiscalPeriodUnknownTests
    // (default-false arm). The middle four arms — Q1, Q2, Q3, Q4 — are
    // currently UNPINNED in the unit test suite. This pin covers Q4, the
    // structurally and operationally most distinct of the four.
    //
    // Why Q4 is the highest-value of the four quarterly arms to pin first:
    //   • Q4 is the annual-period boundary. SEC 10-K reports settle their
    //     final quarter as Q4-tagged facts (some filers tag the standalone
    //     fourth-quarter fact set as Q4, others roll it into FY only).
    //     Misclassification of Q4 facts undercounts the year-end revenue
    //     / EPS / net-income aggregates that every analyst dashboard
    //     keys on for annual comparisons.
    //   • A swap regression — `"Q4" => SecFiscalPeriod.Q3` (copy-paste
    //     from the Q3 arm above it) — would compile, pass the FY pin
    //     (different arm), pass the Unknown pin (different arm), and
    //     silently route every Q4-filed fact into the Q3 bucket. The
    //     last quarter of every filer's fiscal year would appear as a
    //     duplicate Q3, inflating Q3 aggregates and zeroing Q4 ones.
    //   • A drop regression — collapsing the Q4 case into the default
    //     (returning false for "Q4") would silently DROP every standalone-
    //     Q4-tagged fact from the import. Filers like Apple that emit
    //     standalone Q4 facts on the 10-K would simply not appear in the
    //     quarterly drilldowns.
    //   • A "consolidate Q4 into FY" regression — `"Q4" =>
    //     SecFiscalPeriod.FullYear` (intuitive under the false equivalence
    //     "Q4 IS the annual report") — would compile and pass the FY pin
    //     (FY arm untouched), pass the Unknown pin (default untouched),
    //     and merge every Q4 fact with the matching FY fact at the
    //     CollapseToNaturalKey natural-key uniqueness step — silently
    //     deleting one or the other. The dashboard's quarterly trend
    //     chart would show three quarters and a gap.
    //
    // None of these regressions are reachable from the FY or Unknown
    // sibling pins because each arm uses a distinct case label and a
    // distinct return enum value. Only an explicit Q4 → Q4 assertion
    // closes the gap.
    //
    // Pin: invoke with "Q4" (the canonical SEC wire form) and assert
    // BOTH the bool result is true AND the out parameter equals exactly
    // SecFiscalPeriod.Q4. Asserting the enum value distinguishes the
    // working arm from a swap (different enum value, fails) and from a
    // collapse (e.g. FullYear, fails) while passing the FY/Unknown
    // siblings unchanged. Reflection-invoke since the method is private
    // static.
    [Fact]
    public void TryMapFiscalPeriod_Q4Token_ReturnsTrueWithQ4Out()
    {
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryMapFiscalPeriod",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { "Q4", null };
        var result = (bool)method!.Invoke(null, args);

        result.Should().BeTrue();
        args[1].Should().Be(SecFiscalPeriod.Q4);
    }
}
