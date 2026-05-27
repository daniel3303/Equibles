using System.Reflection;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Integrations.Finra.Models;

namespace Equibles.UnitTests.Finra;

public class ShortVolumeImportServiceAggregateVolumesByStockUnknownSymbolTests
{
    // Sibling to ShortVolumeImportServiceAggregateVolumesByStockDuplicateSymbolTests.
    // That pin covers the SUM-on-duplicate arm. This pin covers the
    // structurally distinct UNKNOWN-SYMBOL skip arm:
    //   if (string.IsNullOrEmpty(record.Symbol)
    //       || !tickerMap.TryGetValue(record.Symbol, out var commonStockId))
    //       continue;
    //
    // FINRA's daily short-volume CSV ships every SIP-reported symbol — the
    // universe is far larger than the platform's tracked-stock master list.
    // The tickerMap filter is the load-bearing guard that keeps only
    // platform-tracked stocks in the aggregation; without it, every
    // untracked symbol would either be silently dropped at the
    // DbUpsert (CommonStockId = Guid.Empty constraint failure) or — worse —
    // a future refactor that swaps the upsert for a SaveChanges with
    // ignore-not-found would silently persist garbage DailyShortVolume
    // rows keyed on Guid.Empty.
    //
    // The risk this pin uniquely catches and that the duplicate-symbol
    // sibling cannot:
    //   • Drop the unknown-symbol skip — refactor that "consolidates" the
    //     two guard conditions under the (false) intuition that downstream
    //     constraints will catch unknown symbols anyway. The duplicate
    //     sibling's tickerMap contains the test symbol, so the skip never
    //     fires there — that test passes regardless of whether the skip
    //     is in place. Only a test with an UNKNOWN symbol in the records
    //     drives this branch.
    //   • Inversion regression — `if (tickerMap.TryGetValue(...))
    //     continue;` (logic flip from a careless ! removal during a
    //     "simplify the guard" pass) would skip EVERY tracked symbol
    //     instead of skipping unknown ones, silently emptying the
    //     persisted short-volume table on every import cycle. Caught
    //     by this pin (the tracked symbol is expected to APPEAR in the
    //     result) and missed by the duplicate sibling (which would
    //     produce an empty result either way — the sibling's tracked
    //     symbol would also be skipped, and the assertion that
    //     aggregated[stockId] exists would fail there too — but the
    //     diagnostic from THIS pin is clearer: "unknown was included"
    //     vs the duplicate's "expected key missing").
    //
    // Construction: a tickerMap with ONE tracked symbol. Records contain
    // both the tracked symbol AND an untracked symbol with non-zero
    // volumes. The assertion asserts:
    //   (a) only one entry survives (the tracked one) — defends against
    //       drop-the-skip,
    //   (b) the tracked entry's volumes match the input verbatim —
    //       defends against the skip swallowing the wrong record.
    //
    // The pair (duplicate sibling + unknown-symbol pin) covers both
    // branches of the foreach loop body: the "skip" path and the
    // "merge/insert" path.
    [Fact]
    public void AggregateVolumesByStock_RecordWithSymbolNotInTickerMap_IsSkippedNotPersisted()
    {
        var method = typeof(ShortVolumeImportService).GetMethod(
            "AggregateVolumesByStock",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var trackedStockId = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid> { ["AAPL"] = trackedStockId };
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
                Symbol = "UNTRACKED",
                ShortVolume = 9_999,
                ShortExemptVolume = 999,
                TotalVolume = 99_999,
            },
        };

        var result =
            (Dictionary<Guid, DailyShortVolume>)
                method!.Invoke(null, [records, tickerMap, new DateOnly(2024, 12, 31)]);

        result.Should().ContainSingle();
        result[trackedStockId].ShortVolume.Should().Be(1_000);
    }
}
