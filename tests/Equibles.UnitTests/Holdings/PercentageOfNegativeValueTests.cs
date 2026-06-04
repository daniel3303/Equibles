using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Contract: Percentage.Of's guard only protects the divide against a non-positive
/// TOTAL — it does not (and must not) clamp the numerator. A negative value over a
/// positive total is a legitimate negative percentage (e.g. a reduced position or a
/// quarter-over-quarter decline) and must flow through as a negative number, not be
/// flattened to 0. The existing test pins the zero-total guard; this pins the
/// negative-numerator path. Oracle derived from the "percentage of" contract.
/// </summary>
public class PercentageOfNegativeValueTests
{
    [Fact]
    public void Of_NegativeValueWithPositiveTotal_ReturnsNegativePercentageNotClampedToZero()
    {
        var result = Percentage.Of(-5.0, 20.0);

        result.Should().Be(-25.0);
    }
}
