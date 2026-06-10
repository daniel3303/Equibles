using System.Reflection;
using Equibles.Finra.BusinessLogic;

namespace Equibles.UnitTests.Finra;

/// <summary>
/// Pins the documented contract of ShortSqueezeScoreManager's average-rank
/// percentile normalization: ties share the mean of the ranks they span and a
/// distinct extreme maps to 100 — a three-way tie at the minimum must land at
/// the mean of ranks 0..2 (33.33 on the 0-100 scale), never at 0.
/// </summary>
public class ShortSqueezeScoreManagerPercentilesTests
{
    [Fact]
    public void Percentiles_TiedMinimumSpanningThreeRanks_SharesMeanRankAndMaxMapsTo100()
    {
        var tiedA = Guid.NewGuid();
        var tiedB = Guid.NewGuid();
        var tiedC = Guid.NewGuid();
        var top = Guid.NewGuid();
        var values = new List<(Guid Id, decimal Value)>
        {
            (tiedA, 10m),
            (top, 20m),
            (tiedB, 10m),
            (tiedC, 10m),
        };

        var result = InvokePercentiles(values);

        // Mean of 0-based ranks 0..2 = 1; 1 / (4 - 1) * 100 = 33.33…
        result[tiedA].Should().BeApproximately(100.0 / 3, 1e-9);
        result[tiedB].Should().BeApproximately(100.0 / 3, 1e-9);
        result[tiedC].Should().BeApproximately(100.0 / 3, 1e-9);
        result[top].Should().Be(100, "the distinct maximum maps to the top of the scale");
    }

    private static Dictionary<Guid, double> InvokePercentiles(
        IEnumerable<(Guid Id, decimal Value)> values
    )
    {
        var method = typeof(ShortSqueezeScoreManager).GetMethod(
            "Percentiles",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.Should().NotBeNull("the percentile normalization helper should exist");
        return (Dictionary<Guid, double>)method!.Invoke(null, [values]);
    }
}
