using System.Reflection;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Integrations.Finra.Models;

namespace Equibles.UnitTests.Finra;

public class ShortVolumeImportServiceAggregateVolumesByStockNullSymbolTests
{
    // Third pin in the AggregateVolumesByStock guard family. Two siblings
    // already defend the foreach loop body's branches:
    //   • Duplicate-symbol → SUM (existing duplicate-symbol pin)
    //   • Unknown symbol in tickerMap → SKIP (existing unknown-symbol pin)
    // This pin covers the STRUCTURALLY DISTINCT null-or-empty-Symbol arm of
    // the same guard:
    //   if (string.IsNullOrEmpty(record.Symbol) || !tickerMap.TryGetValue(...))
    //     continue;
    //
    // The unknown-symbol sibling exercises the !TryGetValue half — but with
    // a NON-NULL untracked symbol ("UNTRACKED"). Dropping the
    // IsNullOrEmpty check would compile and pass that sibling
    // (TryGetValue("UNTRACKED") returns false → skip still fires), and
    // pass the duplicate sibling (its Symbol is "AAPL" — guard doesn't
    // fire). Only a NULL Symbol exercises the IsNullOrEmpty half
    // exclusively.
    //
    // The risk this pin uniquely catches:
    //   • Drop the IsNullOrEmpty guard — `tickerMap.TryGetValue(null, ...)`
    //     throws ArgumentNullException on Dictionary<string, Guid>. Real
    //     FINRA daily-short-volume CSVs occasionally emit blank Symbol
    //     cells in malformed rows (typically the summary trailer or a
    //     mid-file corruption); the IsNullOrEmpty branch absorbs them
    //     before they hit the dictionary. Without the guard, a single
    //     bad row throws ANE → propagates up through ImportDate → aborts
    //     the entire day's short-volume ingest with no recovery. The
    //     unknown-symbol pin can't catch this because its test never
    //     supplies a null Symbol.
    //   • Inversion regression — `if (!string.IsNullOrEmpty(...) ||
    //     !TryGetValue(...))` (a stray `!` insertion from a careless
    //     "flip the negation" edit) would skip every NON-empty symbol
    //     instead of the empty ones. The duplicate sibling's
    //     "AAPL" symbol would be skipped, but the sibling test only
    //     asserts on AAPL's aggregation — its empty-result assertion
    //     would fail there. Caught.
    //
    // Pin: feed one record with Symbol=null and assert the aggregator
    // (a) does NOT throw AND (b) returns an empty result. The dual
    // assertion distinguishes:
    //   • Working guard: skip on null, empty result.
    //   • Dropped guard: throws ArgumentNullException (caught by
    //     .NotThrow()).
    //   • Inversion regression: would skip non-null records (also
    //     yields empty result — but the dropped-guard NRE is the
    //     primary risk).
    //
    // The triad (duplicate + unknown + null-symbol) now defends every
    // branch of the foreach loop body — sum path, skip-by-tickermap
    // path, and skip-by-empty-symbol path.
    [Fact]
    public void AggregateVolumesByStock_RecordWithNullSymbol_IsSkippedWithoutThrowing()
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
                Symbol = null,
                ShortVolume = 1_000,
                ShortExemptVolume = 100,
                TotalVolume = 5_000,
            },
        };

        var act = () =>
            (Dictionary<Guid, DailyShortVolume>)
                method!.Invoke(null, [records, tickerMap, new DateOnly(2024, 12, 31)]);

        var result = act.Should().NotThrow().Subject;
        result.Should().BeEmpty();
    }
}
