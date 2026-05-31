using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderTransactionPriceValidatorEvaluateZeroCloseTests
{
    private readonly InsiderTransactionPriceValidator _validator = new();

    // Contract: a real (positive, non-derivative) price that can't yet be
    // checked stays pending (IsPriceValid == null), not valid. A close on file
    // of 0 — bad/sentinel data — is as unusable for validation as a missing
    // close, so it must yield the same pending verdict rather than silently
    // accepting or repairing the price.
    [Fact]
    public void Evaluate_CommonStockWithZeroClose_IsPending()
    {
        var result = _validator.Evaluate(
            reportedPrice: 0.24m,
            shares: 1000,
            kind: InsiderSecurityKind.NonDerivative,
            securityTitle: "Common Stock",
            unadjustedClose: 0m
        );

        result.IsPriceValid.Should().BeNull();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(0.24m);
    }
}
