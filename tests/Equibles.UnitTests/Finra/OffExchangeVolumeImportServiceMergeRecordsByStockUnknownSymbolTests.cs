using System.Reflection;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Integrations.Finra.Models;

namespace Equibles.UnitTests.Finra;

public class OffExchangeVolumeImportServiceMergeRecordsByStockUnknownSymbolTests
{
    // Sibling to the merge pins, covering the tickerMap skip guard:
    //   if (string.IsNullOrEmpty(record.Symbol)
    //       || !tickerMap.TryGetValue(record.Symbol, out var commonStockId))
    //       continue;
    //
    // FINRA's weeklySummary feed reports every OTC/ATS-active symbol — a universe far larger
    // than the platform's tracked-stock master list. The tickerMap filter is the load-bearing
    // guard that keeps only tracked stocks; without it, untracked symbols would map to
    // Guid.Empty and either fail the FK constraint or poison the batch. The risk this uniquely
    // catches: dropping the !TryGetValue skip (or inverting it) would either persist garbage
    // keyed on Guid.Empty or empty the table by skipping every tracked symbol. Pin: a tracked
    // symbol plus an untracked one, asserting only the tracked entity survives with its own
    // numbers intact.
    [Fact]
    public void MergeRecordsByStock_SymbolNotInTickerMap_IsSkippedNotPersisted()
    {
        var method = typeof(OffExchangeVolumeImportService).GetMethod(
            "MergeRecordsByStock",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var trackedStockId = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid> { ["AAPL"] = trackedStockId };
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
                Symbol = "UNTRACKED",
                SummaryTypeCode = "ATS_W_SMBL",
                TotalWeeklyShareQuantity = 9_999,
                TotalWeeklyTradeCount = 99,
            },
        };

        var result =
            (Dictionary<Guid, OffExchangeVolume>)
                method!.Invoke(null, [records, tickerMap, new DateOnly(2024, 3, 4)]);

        result.Should().ContainSingle();
        result[trackedStockId].AtsVolume.Should().Be(1_000);
    }
}
