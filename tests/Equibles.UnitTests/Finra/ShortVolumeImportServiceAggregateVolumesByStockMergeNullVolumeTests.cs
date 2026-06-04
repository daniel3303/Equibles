using System.Reflection;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Integrations.Finra.Models;

namespace Equibles.UnitTests.Finra;

public class ShortVolumeImportServiceAggregateVolumesByStockMergeNullVolumeTests
{
    // Contract: FINRA emits one row per market center for a symbol; the aggregator
    // collapses them by summing, treating a missing (null) per-venue volume cell as
    // 0. The existing null-volume pin uses a SINGLE record (the new-entry branch);
    // this exercises the MERGE branch — a second venue row for an already-seen symbol
    // whose ShortVolume is null must add 0 to the running total, not throw (a
    // `+= record.ShortVolume.Value` would NRE) nor drop the symbol. The other venue's
    // present fields must still sum.
    [Fact]
    public void AggregateVolumesByStock_SecondVenueRowHasNullShortVolume_AddsZeroOnMergeWithoutThrowing()
    {
        var method = typeof(ShortVolumeImportService).GetMethod(
            "AggregateVolumesByStock",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var stockId = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid> { ["AAPL"] = stockId };
        var records = new List<ShortVolumeRecord>
        {
            new()
            {
                Symbol = "AAPL",
                ShortVolume = 1_000,
                ShortExemptVolume = 100,
                TotalVolume = 5_000,
            },
            new()
            {
                Symbol = "AAPL",
                ShortVolume = null,
                ShortExemptVolume = 50,
                TotalVolume = 2_000,
            },
        };

        var result =
            (Dictionary<Guid, DailyShortVolume>)
                method.Invoke(null, [records, tickerMap, new DateOnly(2024, 12, 31)]);

        result.Should().HaveCount(1);
        var aggregated = result[stockId];
        aggregated
            .ShortVolume.Should()
            .Be(1_000, "the null second-venue ShortVolume contributes 0");
        aggregated.ShortExemptVolume.Should().Be(150);
        aggregated.TotalVolume.Should().Be(7_000);
    }
}
