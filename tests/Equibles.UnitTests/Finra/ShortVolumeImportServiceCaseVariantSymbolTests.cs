using System.Reflection;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Integrations.Finra.Models;

namespace Equibles.UnitTests.Finra;

/// <summary>
/// Pins the case-fold collision fix: FINRA writes preferred/when-issued suffixes in
/// lowercase, so <c>TpC</c> is a DIFFERENT security from <c>TPC</c>. The old
/// case-insensitive ticker map folded both onto the common stock and SUMMED them daily.
/// With the ordinal map the case variant misses; the same-symbol multi-venue summing
/// (pinned separately) is unaffected because venue rows repeat the identical spelling.
/// </summary>
public class ShortVolumeImportServiceCaseVariantSymbolTests
{
    private static readonly MethodInfo Aggregate = typeof(ShortVolumeImportService).GetMethod(
        "AggregateVolumesByStock",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    private static readonly MethodInfo CollisionOnly = typeof(ShortVolumeImportService).GetMethod(
        "CollisionOnlyStocks",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Fact]
    public void AggregateVolumesByStock_OrdinalMap_CaseVariantSymbolIsSkippedNotSummed()
    {
        var stockId = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid>(StringComparer.Ordinal) { ["TPC"] = stockId };
        var records = new List<ShortVolumeRecord>
        {
            new()
            {
                Symbol = "TPC",
                ShortVolume = 90_492,
                TotalVolume = 117_265,
            },
            // The preferred security's row — folding it in overstated TPC's volumes.
            new()
            {
                Symbol = "TpC",
                ShortVolume = 5_000,
                TotalVolume = 9_000,
            },
        };

        var result =
            (Dictionary<Guid, DailyShortVolume>)
                Aggregate.Invoke(null, [records, tickerMap, new DateOnly(2026, 8, 4)]);

        result.Should().HaveCount(1);
        result[stockId].ShortVolume.Should().Be(90_492);
        result[stockId].TotalVolume.Should().Be(117_265);
    }

    [Fact]
    public void CollisionOnlyStocks_FileCarriesOnlyTheCaseVariant_FlagsTheStock()
    {
        // The stored row for this day can only have come from the retired case-fold:
        // the file has TpC but no TPC, so the ordinal re-import writes nothing and the
        // corrupt row must be deleted rather than left behind.
        var stockId = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid>(StringComparer.Ordinal) { ["TPC"] = stockId };
        var records = new List<ShortVolumeRecord>
        {
            new() { Symbol = "TpC", ShortVolume = 5_000 },
        };
        var aggregated =
            (Dictionary<Guid, DailyShortVolume>)
                Aggregate.Invoke(null, [records, tickerMap, new DateOnly(2026, 8, 4)]);

        var result = (HashSet<Guid>)CollisionOnly.Invoke(null, [records, tickerMap, aggregated]);

        result.Should().ContainSingle().Which.Should().Be(stockId);
    }

    [Fact]
    public void CollisionOnlyStocks_FileCarriesTheExactSymbolToo_DoesNotFlag()
    {
        // The common stock traded that day: the ordinal re-import overwrites the stored
        // row with the correct TPC-only figures, so nothing may be deleted.
        var stockId = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid>(StringComparer.Ordinal) { ["TPC"] = stockId };
        var records = new List<ShortVolumeRecord>
        {
            new() { Symbol = "TPC", ShortVolume = 90_492 },
            new() { Symbol = "TpC", ShortVolume = 5_000 },
        };
        var aggregated =
            (Dictionary<Guid, DailyShortVolume>)
                Aggregate.Invoke(null, [records, tickerMap, new DateOnly(2026, 8, 4)]);

        var result = (HashSet<Guid>)CollisionOnly.Invoke(null, [records, tickerMap, aggregated]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void CollisionOnlyStocks_ExactSymbolInFileButAbsentFromAggregate_DoesNotFlag()
    {
        // Pins the ordinal-absence clause directly (not via the aggregated guard): if the
        // aggregator ever learns to FILTER records (e.g. dropping zero-volume rows), a stock
        // whose exact symbol is in the file but produced no aggregate must still be protected
        // from deletion — its absence is a filter decision, not a case-fold artifact.
        var stockId = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid>(StringComparer.Ordinal) { ["TPC"] = stockId };
        var records = new List<ShortVolumeRecord>
        {
            new() { Symbol = "TPC", ShortVolume = 0 },
            new() { Symbol = "TpC", ShortVolume = 5_000 },
        };
        var emptyAggregate = new Dictionary<Guid, DailyShortVolume>();

        var result =
            (HashSet<Guid>)CollisionOnly.Invoke(null, [records, tickerMap, emptyAggregate]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void CollisionOnlyStocks_MixedCaseTicker_IsNeverFlaggedForDeletion()
    {
        // Deletion containment: a hypothetical mixed-case stored ticker would permanently
        // miss the ordinal map AND read as "case-variant present, exact absent" every single
        // day — flagging it would delete its rows daily. Confine deletion to the
        // all-uppercase population the case-fold could actually have corrupted.
        var stockId = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid>(StringComparer.Ordinal) { ["TpC"] = stockId };
        var records = new List<ShortVolumeRecord>
        {
            new() { Symbol = "TPC", ShortVolume = 90_492 },
        };
        var aggregated =
            (Dictionary<Guid, DailyShortVolume>)
                Aggregate.Invoke(null, [records, tickerMap, new DateOnly(2026, 8, 4)]);

        var result = (HashSet<Guid>)CollisionOnly.Invoke(null, [records, tickerMap, aggregated]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void CollisionOnlyStocks_FileDoesNotReferenceTheTickerAtAll_DoesNotFlag()
    {
        // A stock the day's file never mentions keeps its stored row — absence from one
        // file is not evidence the row was a collision artifact.
        var tickerMap = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["TPC"] = Guid.NewGuid(),
        };
        var records = new List<ShortVolumeRecord>
        {
            new() { Symbol = "AAPL", ShortVolume = 1_000 },
        };
        var aggregated =
            (Dictionary<Guid, DailyShortVolume>)
                Aggregate.Invoke(null, [records, tickerMap, new DateOnly(2026, 8, 4)]);

        var result = (HashSet<Guid>)CollisionOnly.Invoke(null, [records, tickerMap, aggregated]);

        result.Should().BeEmpty();
    }
}
