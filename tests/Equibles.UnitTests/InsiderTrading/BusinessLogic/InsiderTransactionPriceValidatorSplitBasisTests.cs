using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.BusinessLogic.Models;
using Equibles.InsiderTrading.Data.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

/// <summary>
/// Pins the split-basis awareness behind the misrepair incident: the stored close is on
/// TODAY'S split-adjusted basis while the filed price is on the transaction date's basis, and
/// the old validator compared them as if both were raw — every pre-split filing looked 10×+
/// implausible and got its correct price "repaired" by the share count (15,822 rows, all
/// self-sealed valid; AMZN's pre-20:1 $3,300.24 became $8.42). Also pins the repair band:
/// a repair must reproduce a price inside the session's range on one of the two bases, never
/// fabricate one.
/// </summary>
public class InsiderTransactionPriceValidatorSplitBasisTests
{
    private readonly InsiderTransactionPriceValidator _validator = new();

    [Fact]
    public void Evaluate_PreSplitPriceAgainstAdjustedClose_IsValidAsFiledNotRepaired()
    {
        // The exact production corruption: AMZN 2021-02-16, as-filed $3,300.24, stored close
        // $165.01 on the post-20:1 basis. 3,300.24 / 165.01 = 20× > the 10× cap on the raw
        // basis — but dead-on the close on the as-filed basis (165.01 × 20 = 3,300.20).
        var result = _validator.Evaluate(
            reportedPrice: 3_300.24m,
            shares: 392,
            kind: InsiderSecurityKind.NonDerivative,
            securityTitle: "Common Stock",
            bar: new DailyBarContext
            {
                Close = 165.01m,
                Low = 163.00m,
                High = 166.50m,
                SplitFactorToPresent = 20m,
            }
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result
            .EffectivePrice.Should()
            .Be(3_300.24m, "a correct pre-split price must stay as filed");
    }

    [Fact]
    public void Evaluate_AmbiguousSplitBasis_StaysPending()
    {
        // A captured split whose price adjustment hasn't run: the stored series straddles two
        // bases, so ANY verdict would be a guess off by the ratio — pending, like a missing
        // close, never silently accepted or repaired.
        var result = _validator.Evaluate(
            reportedPrice: 3_300.24m,
            shares: 392,
            kind: InsiderSecurityKind.NonDerivative,
            securityTitle: "Common Stock",
            bar: new DailyBarContext { Close = 165.01m, SplitBasisAmbiguous = true }
        );

        result.IsPriceValid.Should().BeNull();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(3_300.24m);
    }

    [Fact]
    public void Evaluate_GenuineFatFinger_IsStillRepairedInsideTheBand()
    {
        // Total $1,000,000 typed into the per-share field over 20,000 shares → $50/share, the
        // close itself. A real fill inside the session band — repair and stamp it.
        var result = _validator.Evaluate(
            reportedPrice: 1_000_000m,
            shares: 20_000,
            kind: InsiderSecurityKind.NonDerivative,
            securityTitle: "Common Stock",
            bar: new DailyBarContext
            {
                Close = 50m,
                Low = 48m,
                High = 52m,
            }
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.EffectivePrice.Should().Be(50m);
    }

    [Fact]
    public void Evaluate_RepairCandidateOutsideTheBand_IsFlaggedInvalidNotRepaired()
    {
        // $1,000,000 over 1,000 shares → $1,000/share against a $50 close: 20× the close is
        // not a price the market traded at. The old validator published it as a "repair";
        // now the row is flagged and left unrepaired.
        var result = _validator.Evaluate(
            reportedPrice: 1_000_000m,
            shares: 1_000,
            kind: InsiderSecurityKind.NonDerivative,
            securityTitle: "Common Stock",
            bar: new DailyBarContext
            {
                Close = 50m,
                Low = 48m,
                High = 52m,
            }
        );

        result.IsPriceValid.Should().BeFalse();
        result.WasRepaired.Should().BeFalse();
        result
            .EffectivePrice.Should()
            .Be(1_000_000m, "a refused repair must not fabricate a price");
    }

    [Fact]
    public void Evaluate_RepairCandidateInsideTheFactorScaledBand_IsAccepted()
    {
        // A fat-fingered PRE-SPLIT total: $660,048 over 200 shares → $3,300.24, which is the
        // real pre-split fill (close $165.01 × factor 20). The band scales by the factor, so
        // the repair is accepted on the as-filed basis.
        var result = _validator.Evaluate(
            reportedPrice: 660_048m,
            shares: 200,
            kind: InsiderSecurityKind.NonDerivative,
            securityTitle: "Common Stock",
            bar: new DailyBarContext
            {
                Close = 165.01m,
                Low = 163.00m,
                High = 166.50m,
                SplitFactorToPresent = 20m,
            }
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.EffectivePrice.Should().Be(3_300.24m);
    }

    [Fact]
    public void Evaluate_NoUsableRange_FallsBackToTheCloseBand()
    {
        // Bar without Low/High: the band is [close/2, close×2]. $120 on a $50 close is
        // outside it — refused; $80 is inside — repaired.
        var refused = _validator.Evaluate(
            reportedPrice: 120_000m,
            shares: 1_000,
            kind: InsiderSecurityKind.NonDerivative,
            securityTitle: "Common Stock",
            bar: new DailyBarContext { Close = 50m }
        );
        refused.IsPriceValid.Should().BeFalse();
        refused.WasRepaired.Should().BeFalse();

        var repaired = _validator.Evaluate(
            reportedPrice: 80_000m,
            shares: 1_000,
            kind: InsiderSecurityKind.NonDerivative,
            securityTitle: "Common Stock",
            bar: new DailyBarContext { Close = 50m }
        );
        repaired.IsPriceValid.Should().BeTrue();
        repaired.WasRepaired.Should().BeTrue();
        repaired.EffectivePrice.Should().Be(80m);
    }
}
