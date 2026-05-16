using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class StatisticsExtensionsSafeRoundOverflowTests
{
    // Contract: SafeRound is named "Safe" and its doc-comment states its purpose
    // is to "protect view-model casts from OverflowException" on the (decimal)
    // cast. A finite double larger than decimal.MaxValue (~7.9e28) also throws
    // OverflowException on that cast — IsFinite(1e29) is true, so the guard lets
    // it through. (Ambiguity: the literal comment only names NaN/infinity; the
    // method name + stated purpose imply no OverflowException for any double.)
    // A caller relying on "Safe" must get null, not a crashed view render.
    [Fact]
    public void SafeRound_FiniteValueExceedingDecimalRange_ReturnsNullInsteadOfThrowing()
    {
        var result = 1e29.SafeRound(2);

        result.Should().BeNull();
    }
}
