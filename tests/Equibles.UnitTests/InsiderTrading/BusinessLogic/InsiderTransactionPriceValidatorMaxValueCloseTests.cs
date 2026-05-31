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
    [Fact(
        Skip = "GH-3004 — IsPlausible throws OverflowException on a decimal.MaxValue close (close * 10 overflows)"
    )]
    public void IsPlausible_CommonStockWithMaxValueClose_DoesNotThrow()
    {
        var act = () => _validator.IsPlausible(100m, "Common Stock", decimal.MaxValue);

        act.Should().NotThrow();
        _validator.IsPlausible(100m, "Common Stock", decimal.MaxValue).Should().BeTrue();
    }
}
