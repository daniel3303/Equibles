using System.Reflection;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Integrations.Finra.Models;

namespace Equibles.UnitTests.Finra;

public class OffExchangeVolumeImportServiceMergeRecordsByStockMergeAtsAndOtcTests
{
    // OffExchangeVolumeImportService.MergeRecordsByStock is the only path between FINRA's
    // weeklySummary feed and the OffExchangeVolume table. FINRA emits the per-symbol weekly
    // aggregate as TWO distinct rows for the same symbol+week: one tagged ATS_W_SMBL
    // (dark-pool) and one tagged OTC_W_SMBL (non-ATS OTC). The merge must fold both into a
    // single OffExchangeVolume, routing ATS_W_SMBL's share/trade quantities into Ats* and
    // OTC_W_SMBL's into NonAtsOtc*.
    //
    // The risk this pins: a refactor that mis-routes the two summary codes (swaps the
    // branches, drops the else-if, or overwrites instead of merging) would compile and pass
    // any single-row test, then silently report ATS volume as OTC volume (or zero) on every
    // actively-traded symbol. Pin: one ATS row + one OTC row for the same symbol, asserting
    // a single merged entity with all four numbers placed in the correct field.
    [Fact]
    public void MergeRecordsByStock_AtsAndOtcRowsForSameSymbol_MergeIntoOneEntityWithBothPairs()
    {
        var method = typeof(OffExchangeVolumeImportService).GetMethod(
            "MergeRecordsByStock",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var stockId = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid> { ["AAPL"] = stockId };
        var records = new List<OffExchangeWeeklyRecord>
        {
            new()
            {
                Symbol = "AAPL",
                SummaryTypeCode = "ATS_W_SMBL",
                TotalWeeklyShareQuantity = 1_000,
                TotalWeeklyTradeCount = 10,
            },
            new()
            {
                Symbol = "AAPL",
                SummaryTypeCode = "OTC_W_SMBL",
                TotalWeeklyShareQuantity = 2_000,
                TotalWeeklyTradeCount = 20,
            },
        };

        var result =
            (Dictionary<Guid, OffExchangeVolume>)
                method!.Invoke(null, [records, tickerMap, new DateOnly(2024, 3, 4)]);

        result.Should().HaveCount(1);
        var merged = result[stockId];
        merged.AtsVolume.Should().Be(1_000);
        merged.AtsTradeCount.Should().Be(10);
        merged.NonAtsOtcVolume.Should().Be(2_000);
        merged.NonAtsOtcTradeCount.Should().Be(20);
        merged.WeekStartDate.Should().Be(new DateOnly(2024, 3, 4));
    }
}
