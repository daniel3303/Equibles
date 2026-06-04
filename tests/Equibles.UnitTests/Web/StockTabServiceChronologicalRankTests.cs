using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class StockTabServiceChronologicalRankTests
{
    // ChronologicalRank remaps SecFiscalPeriod so the 10-K (FullYear), filed after
    // Q4 and the canonical annual number, sorts AFTER Q4 within a year — the enum
    // ordinal (FullYear=0) would float it to the wrong end. The existing ordering
    // pin mixes FullYear with Q1 (ranks 5 vs 1), which survives a regression that
    // breaks the tight FullYear-vs-Q4 boundary; this pins that adjacent pair.
    [Fact]
    public void ChronologicalRank_FullYear_RanksAboveQ4()
    {
        var method = typeof(StockTabService).GetMethod(
            "ChronologicalRank",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var fullYear = (int)method.Invoke(null, [SecFiscalPeriod.FullYear]);
        var q4 = (int)method.Invoke(null, [SecFiscalPeriod.Q4]);

        fullYear.Should().BeGreaterThan(q4);
    }
}
