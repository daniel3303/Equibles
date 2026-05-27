using System.Reflection;
using Equibles.Integrations.Yahoo.Models;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

public class YahooPriceImportServiceCloseArmOverflowTests
{
    private static readonly MethodInfo HasOverflowPriceMethod =
        typeof(YahooPriceImportService).GetMethod(
            "HasOverflowPrice",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // Continues the HasOverflowPrice 5-arm OR-chain sweep. Existing pins
    // cover Open (1st), High (2nd via prior iteration), AdjustedClose
    // (5th), plus the all-normal "returns false" pin. Low (3rd) and
    // Close (4th) remain unpinned.
    //
    // Close is the natural next pin: it's the most-used downstream
    // field. DailyStockPrice.Close is what every chart, every
    // indicator (SMA, ATR, OBV, Stochastic), and every market-cap
    // calculation reads. A Close overflow corrupts every aggregate
    // and every technical indicator output for the affected ticker
    // for as long as the bad row lives in the DB. Without the Close
    // arm in HasOverflowPrice, the row passes through the import
    // guard and crashes the batch insert with a SqlException
    // ("numeric field overflow"), aborting the entire daily
    // ingestion for the ticker.
    //
    // The risk this pin uniquely catches and that the existing
    // Open/High/AdjustedClose siblings cannot:
    //
    //   • Drop-the-Close-clause regression — `|| Math.Abs(p.Open)
    //     > Max || Math.Abs(p.High) > Max || Math.Abs(p.Low) > Max
    //     || Math.Abs(p.AdjustedClose) > Max` (removed Close) would
    //     compile, pass every sibling pin (those exercise different
    //     fields), and re-introduce the SQL overflow crash for
    //     every Close outlier.
    //
    //   • Wrong-field reference — `Math.Abs(p.High) > Max` repeated
    //     in the Close slot (a copy-paste from the line above)
    //     would render a chain with the right SHAPE but missing
    //     Close coverage. Pinning a price with ONLY Close exceeding
    //     surfaces this — Close-only outliers would slip through
    //     and Open/High/Low/AdjustedClose remain within bounds so
    //     no other arm flags them.
    //
    // Adversarial input: HistoricalPrice with ONLY Close exceeding
    // the numeric(18,4) ceiling. Mirrors the High-arm sibling's
    // shape exactly. The Low arm — the only remaining unpinned
    // arm in the family — would be the next iteration.
    [Fact]
    public void HasOverflowPrice_OnlyCloseExceedsCeiling_ReturnsTrue()
    {
        var price = new HistoricalPrice
        {
            Open = 150.25m,
            High = 152.80m,
            Low = 149.10m,
            Close = 200_000_000_000_000m,
            AdjustedClose = 151.50m,
        };

        var result = (bool)HasOverflowPriceMethod.Invoke(null, [price]);

        result.Should().BeTrue();
    }
}
