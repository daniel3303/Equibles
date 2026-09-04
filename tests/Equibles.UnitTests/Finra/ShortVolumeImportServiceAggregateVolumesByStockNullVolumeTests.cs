using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Integrations.Finra.Models;

namespace Equibles.UnitTests.Finra;

public class ShortVolumeImportServiceAggregateVolumesByStockNullVolumeTests
{
    // The volume fields on a FINRA record are nullable; AggregateVolumesByStock coalesces each with
    // `?? 0`. A record with a TRACKED symbol but null volumes must still aggregate (as 0), never NRE
    // and never be skipped. The existing triad pins the foreach GUARD branches (sum / unknown-symbol
    // / null-symbol), not this volume-null coalesce. Oracle from the contract, not the body.
    [Fact]
    public void AggregateVolumesByStock_MappedRecordWithNullVolumes_AggregatesAsZeroWithoutThrowing()
    {
        var method = typeof(ShortVolumeImportService).GetMethod(
            "AggregateVolumesByStock",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var stockId = Guid.NewGuid();
        var security = new ListedSecurityKey(stockId, "AAPL");
        var tickerMap = new Dictionary<string, ListedSecurityKey> { ["AAPL"] = security };
        var records = new List<ShortVolumeRecord>
        {
            new()
            {
                Symbol = "AAPL",
                ShortVolume = null,
                ShortExemptVolume = null,
                TotalVolume = null,
            },
        };

        var result =
            (Dictionary<ListedSecurityKey, DailyShortVolume>)
                method.Invoke(null, [records, tickerMap, new DateOnly(2024, 9, 30)]);

        result.Should().ContainKey(security);
        result[security].ShortVolume.Should().Be(0);
    }
}
