using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class MarketControllerComputeStatsSkewedSampleTests
{
    // MarketController.ComputeStats is the private helper that powers the
    // /Market/PutCallRatio and /Market/Vix stats panels. It returns a
    // StatsSummary with five distinct named decimals (Mean / Median / Min
    // / Max / StdDev) — five fields, all of type `decimal?`, all read
    // from the SAME MathNet `DescriptiveStatistics` instance with one
    // exception: Median bypasses DescriptiveStatistics and calls
    // `values.Median()` directly (the MathNet extension method).
    //
    // Five-field record structs of compatible types are exactly the
    // shape where a copy-paste refactor silently swaps two fields and
    // the compiler can't catch it. Existing StatisticsExtensions
    // siblings exercise `SafeRound` on individual non-finite doubles;
    // none exercise `ComputeStats` end-to-end with a sample where the
    // Mean and Median DIFFER. Any happy-path sibling using a symmetric
    // input ([1, 2, 3]) sees Mean == Median, so a refactor that
    // accidentally fed `stats.Mean` into BOTH the Mean and Median
    // arguments (a common "tidy" copy-paste error) would still pass
    // that test.
    //
    // Pin a strongly-skewed sample [1.0, 2.0, 10.0]:
    //   Mean   = 4.333... → SafeRound(2) → 4.33
    //   Median = 2.0      → SafeRound(2) → 2.00
    //   Min    = 1.0      → SafeRound(2) → 1.00
    //   Max    = 10.0     → SafeRound(2) → 10.00
    //
    // Mean != Median by a wide margin, so a swap regression
    // (`Mean: values.Median()`, `Median: stats.Mean`) flips the
    // values and the pin fails on both Mean and Median. Asserting
    // Min and Max too lets the test also catch positional swaps in
    // the rest of the record-struct constructor call.
    //
    // ComputeStats is private static and the result is a private
    // nested record struct — reflect on both.
    [Fact]
    public void ComputeStats_SkewedSample_MeanAndMedianAreDistinctFromEachOther()
    {
        var method = typeof(MarketController).GetMethod(
            "ComputeStats",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var summary = method!.Invoke(null, [new[] { 1.0, 2.0, 10.0 }, 2]);

        var type = summary!.GetType();
        decimal? Get(string name) => (decimal?)type.GetProperty(name)!.GetValue(summary);

        Get("Mean").Should().Be(4.33m);
        Get("Median").Should().Be(2m);
        Get("Min").Should().Be(1m);
        Get("Max").Should().Be(10m);
    }
}
