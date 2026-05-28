using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class StockTabServiceChronologicalRankQuarterOrderingTests
{
    // Siblings pin only the FullYear arm and the default safety arm; the four
    // quarter arms are unpinned. Contract: ChronologicalRank reorders periods
    // into real chronological order (the surrounding code notes the enum ordinal
    // is NOT chronological) — Q1<Q2<Q3<Q4, and the annual report sorts after Q4.
    // A transposed/duplicated quarter arm (e.g. Q3 => 2) would break the
    // Financials tab's period ordering; this catches it.
    [Fact]
    public void ChronologicalRank_Quarters_RankStrictlyAscendingAndBeforeFullYear()
    {
        var method = typeof(StockTabService).GetMethod(
            "ChronologicalRank",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        int Rank(SecFiscalPeriod p) => (int)method!.Invoke(null, [p]);

        var q1 = Rank(SecFiscalPeriod.Q1);
        var q2 = Rank(SecFiscalPeriod.Q2);
        var q3 = Rank(SecFiscalPeriod.Q3);
        var q4 = Rank(SecFiscalPeriod.Q4);
        var fullYear = Rank(SecFiscalPeriod.FullYear);

        q1.Should().BeLessThan(q2);
        q2.Should().BeLessThan(q3);
        q3.Should().BeLessThan(q4);
        q4.Should().BeLessThan(fullYear);
    }
}
