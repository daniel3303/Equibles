using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class StockTabServiceChronologicalRankFullYearTests
{
    // Contract (StockTabService.cs:487-497, mirrors the parallel helper in
    // FinancialStatementTools): SecFiscalPeriod's enum ordinal is
    // (FullYear=0, Q1=1, …, Q4=4) — NOT chronological. The 10-K
    // (FullYear) is filed AFTER Q4 standalone and is the canonical
    // annual number, so chronological rank floats FullYear to the END:
    //   Q1=1 < Q2=2 < Q3=3 < Q4=4 < FullYear=5
    // (any unrecognized value → 0, which sorts BEFORE Q1).
    //
    // BuildAvailablePeriods orders periods by `ThenByDescending(
    // ChronologicalRank(...))` so the LATEST period within a year is
    // listed first. The first-element default selection
    // (BuildAvailablePeriods.OrderBy ... .First())  becomes the
    // year's canonical "annual statement" entry — Financials tab's
    // default view.
    //
    // Existing coverage: FinancialStatementTools.ChronologicalRank
    // (the parallel helper in the MCP tool layer) is pinned via
    // FinancialStatementToolsChronologicalRankOrderingTests. The
    // StockTabService.ChronologicalRank private static helper is a
    // structural DUPLICATE — same switch shape, same SecFiscalPeriod
    // enum mapping — but lives in the Web service layer and is
    // currently unpinned. A regression that updates one and forgets
    // the other diverges the MCP tool's period ordering from the
    // Web Financials-tab's period ordering. Operators viewing the
    // same company through the MCP tool ("the FY2024 annual
    // statement is the default") and through the Web Financials tab
    // would see different default selections.
    //
    // Risk surface:
    //   • Drop of the FullYear arm (`SecFiscalPeriod.FullYear => 5`)
    //     would route FullYear through the default `_ => 0` arm,
    //     sorting FY before Q1. The Financials tab would default to
    //     a quarterly statement instead of the 10-K — operators
    //     looking at year-end annual numbers would have to manually
    //     re-select the FullYear period every visit.
    //
    //   • Value swap (`FullYear => 4`, matching Q4): both Q4 and
    //     FullYear would tie in rank, and the secondary sort would
    //     pick whichever appeared first in the source list —
    //     non-deterministic order between the 10-K and the
    //     standalone Q4 quarter.
    //
    //   • Direction inversion in the OrderByDescending call would
    //     also corrupt the ordering, but that's a different surface;
    //     this pin focuses on the rank-value contract.
    //
    // Pin: reflection-invoke ChronologicalRank(FullYear) and assert
    // the exact value 5 — the only arm whose value is structurally
    // load-bearing (Q1-Q4 mapping to their natural numbers is the
    // obvious case; FullYear=5 is the deliberate "after Q4"
    // choice that distinguishes this helper from a naïve int-cast).
    [Fact]
    public void ChronologicalRank_FullYear_RanksAfterQ4()
    {
        var method = typeof(StockTabService).GetMethod(
            "ChronologicalRank",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (int)method!.Invoke(null, [SecFiscalPeriod.FullYear]);

        result.Should().Be(5);
    }
}
