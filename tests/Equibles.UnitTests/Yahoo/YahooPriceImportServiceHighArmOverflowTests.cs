using System.Reflection;
using Equibles.Integrations.Yahoo.Models;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

public class YahooPriceImportServiceHighArmOverflowTests
{
    private static readonly MethodInfo HasOverflowPriceMethod =
        typeof(YahooPriceImportService).GetMethod(
            "HasOverflowPrice",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // Continues the per-arm sweep of HasOverflowPrice's 5-arm OR chain
    // (Open || High || Low || Close || AdjustedClose). Existing pins
    // cover the bookend arms — Open (first) and AdjustedClose (last)
    // — plus the all-normal "returns false" pin. The three INTERIOR
    // arms (High, Low, Close) are unpinned and each could be silently
    // dropped during a refactor without surfacing in any existing test.
    //
    // High is the natural next pin: it's the second arm and the most
    // volatile of the OHLC trio in practice (intraday rallies can push
    // High well above prior session bounds even when Open/Low/Close
    // stay tame). The Yahoo daily feed occasionally emits a corrupted
    // High value — split-adjustment off-by-factor errors are the most
    // common source — that exceeds the numeric(18,4) Postgres column
    // ceiling. Without the High arm in the overflow guard, that single
    // bad row would crash the entire batch insert with a
    // "numeric field overflow" SqlException, aborting the whole
    // daily ingestion for the affected ticker.
    //
    // The risk this pin uniquely catches and that the existing Open
    // and AdjustedClose siblings cannot:
    //
    //   • Drop-the-High-clause regression — `|| Math.Abs(p.Low) > Max
    //     || Math.Abs(p.Close) > Max || Math.Abs(p.AdjustedClose) >
    //     Max` (someone "tidies" what they think is a duplicate
    //     clause) would compile, pass the Open and AdjustedClose
    //     pins (those exercise different fields), and re-introduce
    //     the SQL overflow crash for every High outlier.
    //
    //   • Wrong-field reference — `Math.Abs(p.Open) > Max` repeated
    //     in the High slot (a copy-paste from line 359) would render
    //     a chain that's structurally 5 arms but only checks 4
    //     distinct fields. Pinning a price with High exceeding AND
    //     Open within the ceiling catches this asymmetry — Open
    //     check alone wouldn't flag the row, so a missed-High clause
    //     surfaces as a false-negative.
    //
    // Adversarial input: a price with ONLY High exceeding the
    // numeric(18,4) ceiling — Open/Low/Close/AdjustedClose all
    // within bounds. The result must be true (one arm of the OR
    // chain firing is enough). Mirrors the AdjustedClose sibling's
    // shape exactly for review symmetry.
    [Fact]
    public void HasOverflowPrice_OnlyHighExceedsCeiling_ReturnsTrue()
    {
        var price = new HistoricalPrice
        {
            Open = 150.25m,
            High = 200_000_000_000_000m,
            Low = 149.10m,
            Close = 151.45m,
            AdjustedClose = 150.50m,
        };

        var result = (bool)HasOverflowPriceMethod.Invoke(null, [price]);

        result.Should().BeTrue();
    }
}
