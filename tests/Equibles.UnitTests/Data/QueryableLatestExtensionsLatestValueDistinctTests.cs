using Equibles.Data.Extensions;

namespace Equibles.UnitTests.Data;

public class QueryableLatestExtensionsLatestValueDistinctTests
{
    private sealed record Row(int Year);

    [Fact]
    public void LatestValue_DistinctOverRepeatedProjection_ReturnsSingleHighestValue()
    {
        // Lane B: the existing pin uses the default distinct=false; the distinct=true branch
        // (projected.Distinct() before ORDER BY ... LIMIT 1) was zero-hit. With a projection that
        // repeats values, distinct collapses duplicates and the latest value must still be the max.
        var source = new[]
        {
            new Row(2020),
            new Row(2022),
            new Row(2020),
            new Row(2021),
            new Row(2022),
        }.AsQueryable();

        var result = source.LatestValue(r => r.Year, distinct: true).ToList();

        result.Should().ContainSingle().Which.Should().Be(2022);
    }
}
