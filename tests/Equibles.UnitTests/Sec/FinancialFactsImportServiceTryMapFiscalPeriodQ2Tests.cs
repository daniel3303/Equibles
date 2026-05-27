using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryMapFiscalPeriodQ2Tests
{
    // Family completion (fifth of six arms). FY, Q1, Q4, and Unknown are
    // already pinned by sibling files; this pin covers Q2 — the mid-year
    // tag that filers emit on the 10-Q published in late July / early
    // August. Q3 is the only remaining arm after this iteration.
    //
    // Why Q2 is the right next pin (after Q4 and Q1) in the per-arm
    // sweep: the existing sibling pins defend the two boundary arms (the
    // year-opening Q1 and the year-closing Q4). Q2 sits structurally
    // between them, so a "shift-the-arms-by-one" copy-paste regression
    // (case "Q1" → Q2, case "Q2" → Q3, case "Q3" → Q4 — easy to introduce
    // when someone renames the enum values during a refactor and updates
    // only the case labels but not the return values) would:
    //   • Pass the Q1 sibling pin? NO — Q1 → Q2 fails the Q1 pin's
    //     `args[1].Should().Be(SecFiscalPeriod.Q1)` assertion. Wait —
    //     reading the regression carefully: case "Q1" → Q2 means input
    //     "Q1" returns Q2 enum. Q1 sibling pin asserts Q1 wire → Q1
    //     enum, so it would FAIL there. So the shift regression actually
    //     IS caught by the Q1 pin.
    //   • So the shift regression is already covered. What this Q2 pin
    //     uniquely catches is an INDEPENDENT swap of just the Q2 arm —
    //     `case "Q2" => SecFiscalPeriod.Q4` (intuitive under "Q2 is
    //     half-year, so map to FullYear-adjacent value" mental confusion)
    //     — which would compile, pass Q1 + Q4 + FY + Unknown (each
    //     untouched), and silently misclassify every Q2-filed fact.
    //   • Mid-year filings are the period operators most often query for
    //     the "half-year revenue" comparison; corruption here distorts
    //     the YoY trend lines that institutional dashboards use as the
    //     early-warning signal for revenue deceleration.
    //
    // Pin: invoke with "Q2" and assert BOTH the bool result is true AND
    // the out parameter equals exactly SecFiscalPeriod.Q2. Reflection-
    // invoke since the method is private static.
    //
    // After this iteration: 5/6 arms defended (FY, Q1, Q2, Q4, Unknown).
    // Q3 remains as the final arm to round out exhaustive per-arm
    // coverage.
    [Fact]
    public void TryMapFiscalPeriod_Q2Token_ReturnsTrueWithQ2Out()
    {
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryMapFiscalPeriod",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { "Q2", null };
        var result = (bool)method!.Invoke(null, args);

        result.Should().BeTrue();
        args[1].Should().Be(SecFiscalPeriod.Q2);
    }
}
