using System.Reflection;
using Equibles.Finra.BusinessLogic;

namespace Equibles.UnitTests.Finra;

public class ShortSqueezeScoreManagerPercentilesSingleEntryTests
{
    [Fact]
    public void Percentiles_SingleEntry_SitsAtMidpoint()
    {
        // Contract: "a single-entry set sits at 50". With no peers to rank against the lone
        // name lands at the midpoint, and the special case guards the general average-rank
        // formula's divide-by-(Count - 1), which would be 0 / 0 = NaN at a count of one.
        var only = Guid.NewGuid();
        var values = new List<(Guid Id, decimal Value)> { (only, 42m) };

        var result = InvokePercentiles(values);

        result[only].Should().Be(50);
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
