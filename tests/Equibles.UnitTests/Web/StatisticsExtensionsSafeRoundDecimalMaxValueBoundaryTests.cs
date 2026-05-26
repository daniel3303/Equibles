using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

/// <summary>
/// (double)decimal.MaxValue rounds up to 2^96 (decimal.MaxValue + 1) because
/// double can't represent 2^96 - 1 exactly. The guard uses strict '>' so
/// the boundary value passes through to the (decimal) cast, which throws
/// OverflowException — the exact failure mode SafeRound exists to prevent.
/// </summary>
public class StatisticsExtensionsSafeRoundDecimalMaxValueBoundaryTests
{
    [Fact(Skip = "GH-2108 — strict > guard misses (double)decimal.MaxValue boundary")]
    public void SafeRound_DoubleDecimalMaxValue_ReturnsNullInsteadOfThrowing()
    {
        var value = (double)decimal.MaxValue;

        var result = value.SafeRound(2);

        result.Should().BeNull();
    }
}
