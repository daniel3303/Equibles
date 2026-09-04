using Equibles.CommonStocks.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Integrations.Finra.Models;

namespace Equibles.UnitTests.Finra;

public class OffExchangeVolumeImportServiceMergeRecordsByStockNullQuantityTests
{
    // The share/trade quantities on a FINRA weeklySummary record are nullable Numbers;
    // MergeRecordsByStock coalesces each with `?? 0`. A row for a TRACKED symbol with null
    // quantities must still produce an entity (with the affected pair at 0), never NRE and
    // never be dropped. The existing merge/skip pins don't exercise the null-coalesce — a
    // refactor that reads record.TotalWeeklyShareQuantity.Value directly (dropping the ?? 0)
    // would throw on FINRA's occasional null cells and abort the whole week's ingest. Oracle
    // from the contract, not the body.
    [Fact]
    public void MergeRecordsByStock_TrackedSymbolWithNullQuantities_MergesAsZeroWithoutThrowing()
    {
        var stockId = Guid.NewGuid();
        var security = new ListedSecurityKey(stockId, "AAPL");
        var tickerMap = new Dictionary<string, ListedSecurityKey> { ["AAPL"] = security };
        var records = new List<OffExchangeWeeklyRecord>
        {
            new()
            {
                Symbol = "AAPL",
                SummaryTypeCode = "ATS_W_SMBL",
                TotalWeeklyShareQuantity = null,
                TotalWeeklyTradeCount = null,
            },
        };

        var act = () =>
            OffExchangeVolumeMerger.Merge(
                records,
                tickerMap,
                new Dictionary<string, ListedSecurityKey>(),
                new DateOnly(2024, 3, 4)
            );

        var result = act.Should().NotThrow().Subject;
        result.Should().ContainKey(security);
        result[security].AtsVolume.Should().Be(0);
        result[security].AtsTradeCount.Should().Be(0);
    }
}
