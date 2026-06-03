using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

// The ADS/ADR unit-mismatch correction wired through the price evaluator: a
// per-ADS price on an ordinary-share count is restated to per-ordinary so
// Shares × EffectivePrice is a real dollar value, while the as-filed per-ADS
// figure (ReportedPricePerShare) is left for the caller to preserve.
public class InsiderTransactionPriceValidatorAdsTests
{
    private readonly InsiderTransactionPriceValidator _validator = new();

    private static readonly string[] SvreNotes =
    {
        "The price reported is the price per American Depositary Share (\"ADS\") acquired in an open-market transaction on The Nasdaq Stock Market LLC. Each ADS represents 43,200 ordinary shares of the Issuer pursuant to the ADS ratio effective February 25, 2026. The Reporting Person acquired 57,907 ADSs on April 9, 2026 at $3.45, all per ADS, resulting in the underlying ordinary shares reported.",
    };

    // The headline case: 2,501,582,400 ordinary × $3.45/ADS read as $8.6B; with
    // 1 ADS = 43,200 ordinary the effective per-ordinary price restates the value
    // to ~$200K. Close ($3.45, the ADS price) is well above the per-ordinary
    // price, so the row is plausible and never spuriously "repaired".
    [Fact]
    public void Evaluate_PerAdsPriceOnOrdinaryCount_RestatesPriceToPerOrdinary()
    {
        var evaluation = _validator.Evaluate(
            reportedPrice: 3.45m,
            shares: 2_501_582_400L,
            kind: InsiderSecurityKind.NonDerivative,
            securityTitle: "Ordinary Shares",
            unadjustedClose: 3.45m,
            notes: SvreNotes
        );

        evaluation.IsPriceValid.Should().BeTrue();
        evaluation.WasRepaired.Should().BeFalse();
        evaluation.EffectivePrice.Should().Be(3.45m / 43_200);
        (evaluation.EffectivePrice * 2_501_582_400L).Should().BeApproximately(199_779.6m, 1m);
    }

    // Without the footnotes the evaluator can't know the price is per ADS, so it
    // leaves the value untouched — the correction is footnote-driven, not
    // inferred from the numbers.
    [Fact]
    public void Evaluate_PerAdsPriceWithoutNotes_LeavesPriceUnchanged()
    {
        var evaluation = _validator.Evaluate(
            reportedPrice: 3.45m,
            shares: 2_501_582_400L,
            kind: InsiderSecurityKind.NonDerivative,
            securityTitle: "Ordinary Shares",
            unadjustedClose: 3.45m,
            notes: null
        );

        evaluation.EffectivePrice.Should().Be(3.45m);
    }

    // A plain domestic common-stock sale must be unaffected by the new notes
    // path — no ADS footnote, price stays as filed.
    [Fact]
    public void Evaluate_PlainCommonStock_IsUnaffectedByAdsPath()
    {
        var evaluation = _validator.Evaluate(
            reportedPrice: 50m,
            shares: 1_000L,
            kind: InsiderSecurityKind.NonDerivative,
            securityTitle: "Common Stock",
            unadjustedClose: 49m,
            notes: new[] { "Represents shares withheld to cover taxes." }
        );

        evaluation.IsPriceValid.Should().BeTrue();
        evaluation.EffectivePrice.Should().Be(50m);
    }
}
