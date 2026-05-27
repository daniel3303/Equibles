using System.Reflection;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

public class StockPriceToolsExtractHighLowCloseProjectionAlignmentTests
{
    // ExtractHighLowClose is the third private-static helper in
    // StockPriceTools (alongside StartTable and AppendNewestFirstRows,
    // both pinned). It returns a tuple of three `List<decimal>`
    // projections, ALL picking from the same `records` list but each
    // selecting a DIFFERENT decimal column:
    //     records.Select(p => p.High),
    //     records.Select(p => p.Low),
    //     records.Select(p => p.Close)
    //
    // The risks this pin uniquely catches and the existing siblings
    // cannot:
    //
    //   • Tuple-slot misrouting — three projection arms, each could
    //     point at the wrong source property. A copy-paste mistake
    //     (`Highs = records.Select(p => p.Low)`) would compile cleanly
    //     because every DailyStockPrice price column is `decimal`;
    //     the compiler can't distinguish Open/High/Low/Close at the
    //     type level. The output tuple's NAMES say "Highs" but the
    //     values are Lows — the downstream `ComputeAtr`,
    //     `ComputeStochasticOscillator`, `ComputeObv` functions
    //     receive their inputs by tuple POSITION, so the calculation
    //     would silently invert ranges (ATR using Low as High would
    //     never compute a sensible volatility).
    //
    //   • Wrong source column — `records.Select(p => p.Open)` instead
    //     of `p.High` (Open is alphabetically adjacent and the most
    //     common copy-paste source). The High-Low ATR calculation
    //     would degrade to the Open-Low range — visually plausible
    //     numbers but wrong.
    //
    //   • Property-rename drift — if DailyStockPrice ever renames
    //     `.High` → `.HighPrice`, the compile error surfaces here
    //     immediately rather than after a silent backfill of the
    //     ATR/Stoch series with wrong values. The existing siblings
    //     don't touch DailyStockPrice properties.
    //
    // Adversarial input: a single record with three DISTINCT values
    // for High (100), Low (80), Close (90) — picked so a copy-paste
    // misroute would visibly fail the per-slot assertion. The Open
    // value is also distinct (50) to surface the Open-for-High swap
    // class specifically.
    //
    // Assertion strategy: invoke the private-static helper via
    // reflection; the returned tuple is a `(List<decimal> Highs,
    // List<decimal> Lows, List<decimal> Closes)` value tuple, but
    // reflection erases the field names — so we read .Item1/.Item2/
    // .Item3 by position, which is exactly how the production callers
    // consume the result.
    [Fact]
    public void ExtractHighLowClose_RecordWithDistinctOhlcValues_RoutesEachColumnToCorrespondingTupleSlot()
    {
        var method = typeof(StockPriceTools).GetMethod(
            "ExtractHighLowClose",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var records = new List<DailyStockPrice>
        {
            new DailyStockPrice
            {
                Open = 50m,
                High = 100m,
                Low = 80m,
                Close = 90m,
            },
        };

        var result =
            (ValueTuple<List<decimal>, List<decimal>, List<decimal>>)
                method!.Invoke(null, [records]);

        result.Item1.Should().Equal(100m);
        result.Item2.Should().Equal(80m);
        result.Item3.Should().Equal(90m);
    }
}
