using System.Reflection;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Integrations.Finra.Models;

namespace Equibles.UnitTests.Finra;

public class OffExchangeVolumeImportServiceMergeRecordsByStockAtsOnlyTests
{
    // Sibling to the ATS+OTC merge pin. FINRA does not guarantee both summary rows exist for
    // every symbol in a given week: a security may trade on ATS venues only (no non-ATS OTC
    // activity), so the feed ships an ATS_W_SMBL row with no matching OTC_W_SMBL row. The
    // merge must still produce an entity, leaving the missing pair at its zero default rather
    // than dropping the stock or carrying stale numbers.
    //
    // The risk this uniquely catches: a refactor that only persists a stock once BOTH rows
    // are seen (e.g. requiring the OTC branch to have fired before adding to the dictionary)
    // would silently omit every ATS-only symbol. Pin: a single ATS_W_SMBL row, asserting the
    // entity exists with Ats* populated and NonAtsOtc* left at zero.
    [Fact]
    public void MergeRecordsByStock_AtsRowWithNoOtcRow_YieldsEntityWithNonAtsOtcZero()
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
                TotalWeeklyShareQuantity = 5_000,
                TotalWeeklyTradeCount = 50,
            },
        };

        var result =
            (Dictionary<Guid, OffExchangeVolume>)
                method!.Invoke(null, [records, tickerMap, new DateOnly(2024, 3, 4)]);

        result.Should().ContainSingle();
        var merged = result[stockId];
        merged.AtsVolume.Should().Be(5_000);
        merged.AtsTradeCount.Should().Be(50);
        merged.NonAtsOtcVolume.Should().Be(0);
        merged.NonAtsOtcTradeCount.Should().Be(0);
    }
}
