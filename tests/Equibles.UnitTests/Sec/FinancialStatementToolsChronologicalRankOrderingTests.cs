using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialStatementToolsChronologicalRankOrderingTests
{
    // ChronologicalRank's body comment commits to a precise ordering invariant:
    // "Q1 < Q2 < Q3 < Q4 < FullYear". The function feeds an OrderByDescending
    // tie-break that picks the latest period within a fiscal year — a Q3/Q4
    // swap (or any reordering) would silently surface the wrong period as the
    // "latest" when both arrive in the same year, returning Q3 figures for a
    // user that asked for FY/latest. The default arm returns 0, so an unknown
    // enum value would sort before Q1 — fine for stability. Pin the explicit
    // ordering across every defined value in one shot.
    [Fact]
    public void ChronologicalRank_AllDefinedPeriods_ProduceStrictlyAscendingRanksQ1ToFullYear()
    {
        var method = typeof(FinancialStatementTools).GetMethod(
            "ChronologicalRank",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var q1 = (int)method.Invoke(null, [SecFiscalPeriod.Q1]);
        var q2 = (int)method.Invoke(null, [SecFiscalPeriod.Q2]);
        var q3 = (int)method.Invoke(null, [SecFiscalPeriod.Q3]);
        var q4 = (int)method.Invoke(null, [SecFiscalPeriod.Q4]);
        var fy = (int)method.Invoke(null, [SecFiscalPeriod.FullYear]);

        (q1 < q2 && q2 < q3 && q3 < q4 && q4 < fy)
            .Should()
            .BeTrue($"docstring promises Q1<Q2<Q3<Q4<FullYear; got {q1},{q2},{q3},{q4},{fy}");
    }
}
