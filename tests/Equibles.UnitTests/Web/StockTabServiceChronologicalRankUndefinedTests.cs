using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class StockTabServiceChronologicalRankUndefinedTests
{
    // Sibling to StockTabServiceChronologicalRankFullYearTests. That sibling
    // pins the FullYear arm (the "load-bearing chronological override"
    // case). The default-arm `_ => 0` is the safety net for any value not
    // in the SecFiscalPeriod enum — currently an enum extension is
    // hypothetical, but production-real on an EF migration that ships
    // a new enum value (e.g. a hypothetical TTM/Trailing-Twelve-Months
    // period) before the period-ordering code is updated.
    //
    // The risks this pin uniquely catches and the FullYear sibling
    // cannot:
    //   • Drop the `_ => 0` arm — a C# switch expression without a
    //     matching arm throws `SwitchExpressionException` at runtime.
    //     The Financials tab would 500 immediately on any unknown
    //     period value, instead of degrading gracefully (sorted to
    //     the front of the list with a missing label).
    //   • A "tighten the enum coverage" refactor that throws explicitly
    //     (`_ => throw new InvalidOperationException(...)`) — same
    //     observable symptom: 500 instead of graceful degradation.
    //   • A regression that changes the default value (`_ => -1`,
    //     `_ => int.MaxValue`) — would reorder unknown periods
    //     unexpectedly across the list.
    //
    // Strategy: cast an out-of-range integer to SecFiscalPeriod (the
    // enum has values 0–4, so 99 is undefined) and reflection-invoke
    // ChronologicalRank. Expect 0 — the documented safety default.
    [Fact]
    public void ChronologicalRank_UndefinedEnumValue_ReturnsZeroSafetyDefault()
    {
        var method = typeof(StockTabService).GetMethod(
            "ChronologicalRank",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (int)method!.Invoke(null, [(SecFiscalPeriod)99]);

        result.Should().Be(0);
    }
}
