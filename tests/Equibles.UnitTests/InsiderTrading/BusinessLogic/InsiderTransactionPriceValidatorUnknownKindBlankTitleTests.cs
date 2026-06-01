using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderTransactionPriceValidatorUnknownKindBlankTitleTests
{
    private readonly InsiderTransactionPriceValidator _validator = new();

    // Contract: an Unknown kind falls back to the title-keyword heuristic, and a
    // blank/whitespace title can't be classified — so it must default to
    // non-derivative, leaving the implausible-price validation in force. The
    // sibling pin covers a derivative-titled Unknown row (waved through, unrepaired);
    // this pins the opposite — a whitespace title must NOT exempt the row from
    // validation. Were the blank-title branch flipped to "treat unclassifiable as
    // derivative", a mis-entered total-as-per-share price on an untitled, not-yet-
    // reclassified row would pass unrepaired, defeating the validator's whole purpose.
    [Fact]
    public void Evaluate_UnknownKindWithWhitespaceTitle_TreatsAsNonDerivativeAndRepairs()
    {
        var result = _validator.Evaluate(
            reportedPrice: 50_000m,
            shares: 1000,
            kind: InsiderSecurityKind.Unknown,
            securityTitle: "   ",
            unadjustedClose: 50m
        );

        // Repaired (not waved through as a derivative): the mis-entered total is
        // divided by the share count to recover the unit price.
        result.WasRepaired.Should().BeTrue();
        result.IsPriceValid.Should().Be(true);
        result.EffectivePrice.Should().Be(50m);
    }
}
