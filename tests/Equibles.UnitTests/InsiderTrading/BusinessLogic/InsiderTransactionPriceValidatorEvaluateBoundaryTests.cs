using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderTransactionPriceValidatorEvaluateBoundaryTests
{
    private readonly InsiderTransactionPriceValidator _validator = new();

    [Fact]
    public void Evaluate_PriceAtExactlyTenTimesClose_IsValidAndUnrepaired()
    {
        // Contract: reject only when the price exceeds the close by MORE THAN 10x, so a price
        // sitting EXACTLY at 10x is the inclusive ceiling — valid and kept as-filed, never
        // repaired. Evaluate carries its own copy of the 10x rule; only IsPlausible's boundary
        // is pinned today, so a drift to a strict `<` here would silently repair 500 -> 0.5.
        var result = _validator.Evaluate(
            reportedPrice: 500m,
            shares: 1000,
            kind: InsiderSecurityKind.NonDerivative,
            securityTitle: "Common Stock",
            unadjustedClose: 50m
        );

        result.IsPriceValid.Should().Be(true);
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(500m);
    }
}
