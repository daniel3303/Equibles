using System.Reflection;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Integrations.Finra.Models;

namespace Equibles.UnitTests.Finra;

public class ShortVolumeImportServiceAggregateVolumesByStockDuplicateSymbolTests
{
    [Fact]
    public void AggregateVolumesByStock_SameSymbolTwiceInBatch_SumsVolumesRatherThanOverwriting()
    {
        // ShortVolumeImportService.AggregateVolumesByStock (extracted in #1477)
        // is the only path between FINRA's daily short-volume API and the
        // DailyShortVolume table. FINRA's feed routinely emits multiple rows
        // for the same SIP symbol within a single day — one per market center
        // (NASDAQ ADF, FINRA/NYSE TRF, FINRA/NASDAQ TRF, etc.). The aggregator
        // must collapse those rows by summing ShortVolume / ShortExemptVolume
        // / TotalVolume so the persisted total reflects all-venue activity.
        //
        // The risk this catches: a refactor that "simplifies" the
        //   if (aggregated.TryGetValue(commonStockId, out var existing)) {
        //       existing.ShortVolume += record.ShortVolume ?? 0; ...
        //   } else {
        //       aggregated[commonStockId] = new DailyShortVolume { ... };
        //   }
        // branch by collapsing both arms into an unconditional
        //   aggregated[commonStockId] = new DailyShortVolume { ... };
        // (under the false intuition that "the dictionary handles dedup
        // itself") would compile, pass any test that doesn't repeat a
        // ticker within a batch, and silently OVERWRITE rather than SUM.
        // Result: the persisted DailyShortVolume reflects only the LAST
        // venue's row for any multi-venue symbol — under-reporting short
        // volume by 50%+ on every actively-traded large-cap.
        //
        // Pin: two records for the same Symbol with non-overlapping numeric
        // values. The result must contain a single entry whose volumes are
        // the SUM, not the second record alone.
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
                ShortVolume = 2_000,
                ShortExemptVolume = 200,
                TotalVolume = 10_000,
            },
        };

        var result =
            (Dictionary<Guid, DailyShortVolume>)
                method.Invoke(null, [records, tickerMap, new DateOnly(2024, 12, 31)]);

        result.Should().HaveCount(1);
        var aggregated = result[stockId];
        aggregated.ShortVolume.Should().Be(3_000);
        aggregated.ShortExemptVolume.Should().Be(300);
        aggregated.TotalVolume.Should().Be(15_000);
    }
}
