using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderTransactionPriceValidatorMaxValueCloseTests
{
    private readonly InsiderTransactionPriceValidator _validator = new();

    // Contract: IsPlausible classifies a reported price against the close and
    // returns a bool — it must never throw for a valid decimal close. The
    // ceiling check computes close * 10; a decimal.MaxValue close makes that
    // product overflow, so an extreme-but-legal close must still yield a
    // verdict (a price below any sane multiple of MaxValue is plausible),
    // not an OverflowException leaking out of the validator.
    [Fact]
    public void IsPlausible_CommonStockWithMaxValueClose_DoesNotThrow()
    {
        var act = () => _validator.IsPlausible(100m, "Common Stock", decimal.MaxValue);

        act.Should().NotThrow();
        _validator.IsPlausible(100m, "Common Stock", decimal.MaxValue).Should().BeTrue();
    }

    // Evaluate shares the same close * 10 ceiling check (the plausible-against-
    // close branch), so a decimal.MaxValue close overflows there too. The
    // contract is to return a verdict — a price far below any sane multiple of
    // a MaxValue close is valid as filed, not an OverflowException.
    [Fact]
    public void Evaluate_CommonStockWithMaxValueClose_DoesNotThrow()
    {
        var act = () =>
            _validator.Evaluate(
                reportedPrice: 100m,
                shares: 10,
                kind: InsiderSecurityKind.NonDerivative,
                securityTitle: "Common Stock",
                unadjustedClose: decimal.MaxValue
            );

        act.Should().NotThrow();
        var evaluation = act();
        evaluation.IsPriceValid.Should().Be(true);
        evaluation.EffectivePrice.Should().Be(100m);
        evaluation.WasRepaired.Should().BeFalse();
    }
}
