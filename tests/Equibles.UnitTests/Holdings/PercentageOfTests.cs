using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins Percentage.Of's divide-by-zero guard. The doc-comment promises a zero (or
/// empty) total yields 0% rather than NaN/Infinity — an unguarded double divide would
/// make 5/0*100 evaluate to +Infinity and poison every downstream allocation/overlap
/// percentage that flows from an empty portfolio. Oracle derived from the contract.
/// </summary>
public class PercentageOfTests
{
    [Fact]
    public void Of_ZeroTotalWithNonZeroValue_ReturnsZeroNotInfinity()
    {
        var result = Percentage.Of(5.0, 0.0);

        result.Should().Be(0);
    }
}
