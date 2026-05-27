using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryMapFiscalPeriodQ3Tests
{
    // Final arm in the TryMapFiscalPeriod sweep. FY, Q1, Q2, Q4, and the
    // Unknown default are pinned by sibling files; this pin covers Q3,
    // completing exhaustive per-arm coverage of all six switch cases.
    //
    // Why Q3 closes the sweep: with every other case individually pinned,
    // the Q3 arm is the last branch where a swap or drop could compile
    // and slip through unnoticed. The neighbouring siblings each defend
    // their own enum-return value, so:
    //   • A Q3 → Q4 swap (the most likely copy-paste, given Q4 sits
    //     adjacent in source) would compile, pass FY/Q1/Q2/Q4/Unknown
    //     (each untouched), and silently MERGE every Q3-filed fact
    //     into the Q4 bucket. Filers like Apple emit Q3 on the late-
    //     July / early-August 10-Q and Q4 standalone on the 10-K —
    //     collapsing Q3 into Q4 would inflate Q4 aggregates and zero
    //     Q3 ones, hiding the seasonal Q3 → Q4 step-up that retail-
    //     heavy filers actually produce.
    //   • A Q3 → FullYear collapse (intuitive under "we already have
    //     three quarters' worth of data by Q3, so map to annual?")
    //     would compile, pass every other arm pin, and silently
    //     deduplicate Q3 facts at CollapseToNaturalKey against the
    //     FY-tagged annual fact for the same period.
    //   • A drop-into-default would silently elide every Q3 fact from
    //     the importer entirely.
    //
    // Q3 is the only arm where these regressions remain unreachable
    // from the existing five sibling pins. After this iteration, the
    // entire TryMapFiscalPeriod switch has individual per-arm pins —
    // any single-arm corruption fails on the corresponding sibling.
    //
    // Pin: invoke with "Q3" (canonical SEC wire form) and assert BOTH
    // the bool result is true AND the out parameter equals exactly
    // SecFiscalPeriod.Q3. Reflection-invoke since private static.
    [Fact]
    public void TryMapFiscalPeriod_Q3Token_ReturnsTrueWithQ3Out()
    {
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryMapFiscalPeriod",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { "Q3", null };
        var result = (bool)method!.Invoke(null, args);

        result.Should().BeTrue();
        args[1].Should().Be(SecFiscalPeriod.Q3);
    }
}
