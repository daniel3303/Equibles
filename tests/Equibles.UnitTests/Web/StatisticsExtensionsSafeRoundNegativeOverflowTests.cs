using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class StatisticsExtensionsSafeRoundNegativeOverflowTests
{
    [Fact]
    public void SafeRound_NegativeFiniteValueBelowDecimalRange_ReturnsNullInsteadOfThrowing()
    {
        // Sibling pin to StatisticsExtensionsSafeRoundOverflowTests, which covers
        // only the positive-overflow arm (1e29 > decimal.MaxValue). The production
        // guard is bi-directional:
        //   if (!double.IsFinite(value)
        //       || value > (double)decimal.MaxValue
        //       || value < (double)decimal.MinValue)
        //       return null;
        // The third condition — `value < (double)decimal.MinValue` — is the
        // symmetric negative-overflow arm. Without an isolated pin, a refactor
        // that "tidies" the guard to just `value > (double)decimal.MaxValue`
        // (under the false intuition that "overflow only goes one way") would
        // compile, pass the existing +1e29 pin, and throw OverflowException
        // on the (decimal) cast for any large negative finite — for example a
        // runaway variance calculation in DescriptiveStatistics or a
        // pathological FRED observation whose unit conversion produced
        // -1e30. The Show action's `ComputeStats` path calls SafeRound on
        // every Min/Max/Mean/Median/StdDev, and any one of them crashing
        // the view render is the failure mode the "Safe" prefix exists to
        // prevent.
        //
        // Pin -1e29 (just past decimal.MinValue ≈ -7.9e28). Both the +1e29
        // sibling and this one together prove the bi-directional guard fires.
        var result = (-1e29).SafeRound(2);

        result.Should().BeNull();
    }
}
