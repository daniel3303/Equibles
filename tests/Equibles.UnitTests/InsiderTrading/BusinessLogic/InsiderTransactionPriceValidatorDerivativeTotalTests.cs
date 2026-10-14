using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.BusinessLogic.Models;
using Equibles.InsiderTrading.Data.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

/// <summary>
/// Pins the one-for-one derivative repair: a total premium typed into the per-unit field (accession
/// 0001104659-26-097430, $3,659,027 per option on 183,640 options) is divided back to the unit price.
/// Without the filing's own 1:1 evidence, or without a settled close, a derivative stands as filed.
/// </summary>
public class InsiderTransactionPriceValidatorDerivativeTotalTests
{
    private readonly InsiderTransactionPriceValidator _validator = new();

    private InsiderTransactionPriceEvaluation EvaluateDerivative(
        decimal reportedPrice,
        long shares,
        DailyBarContext bar,
        long? underlyingShares,
        decimal? exercisePrice,
        string securityTitle = "Call option (obligation to sell)"
    ) =>
        _validator.Evaluate(
            reportedPrice,
            shares,
            InsiderSecurityKind.Derivative,
            securityTitle,
            bar,
            notes: null,
            underlyingShares: underlyingShares,
            exercisePrice: exercisePrice
        );

    [Fact]
    public void Evaluate_OneForOneCallWithTotalPremiumInUnitField_RepairsToThePerOptionPrice()
    {
        var result = EvaluateDerivative(
            reportedPrice: 3_659_027m,
            shares: 183_640,
            bar: new DailyBarContext { Close = 108m },
            underlyingShares: 183_640,
            exercisePrice: 130m
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.EffectivePrice.Should().Be(3_659_027m / 183_640m);
        result.EffectivePrice.Should().BeApproximately(19.925m, 0.001m);
    }

    [Fact]
    public void Evaluate_OneForOneCallWithRealPremium_IsValidAsFiled()
    {
        var result = EvaluateDerivative(
            reportedPrice: 20m,
            shares: 183_640,
            bar: new DailyBarContext { Close = 108m },
            underlyingShares: 183_640,
            exercisePrice: 130m
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(20m);
    }

    [Fact]
    public void Evaluate_ConvertiblePreferredWithStatedValueNotOneForOne_IsValidAsFiled()
    {
        // 5,000 preferred shares at their $10,000 stated value, each converting into 100 commons at
        // $100: far above the ceiling, so only the missing one-for-one evidence keeps it as filed.
        var result = EvaluateDerivative(
            reportedPrice: 10_000m,
            shares: 5_000,
            bar: new DailyBarContext { Close = 10m },
            underlyingShares: 500_000,
            exercisePrice: 100m,
            securityTitle: "Series A Convertible Preferred Stock"
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(10_000m);
    }

    [Fact]
    public void Evaluate_ConvertibleNoteCountedByPrincipal_IsValidAsFiled()
    {
        // The share count is the $5M principal and the price is the principal too; the note
        // converts into 2.5M shares, so there is no one-for-one evidence to repair against.
        var result = EvaluateDerivative(
            reportedPrice: 5_000_000m,
            shares: 5_000_000,
            bar: new DailyBarContext { Close = 1.20m },
            underlyingShares: 2_500_000,
            exercisePrice: 2m,
            securityTitle: "Convertible Promissory Note"
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(5_000_000m);
    }

    [Fact]
    public void Evaluate_DerivativeWithoutUnderlyingShares_IsValidAsFiled()
    {
        var result = EvaluateDerivative(
            reportedPrice: 3_659_027m,
            shares: 183_640,
            bar: new DailyBarContext { Close = 108m },
            underlyingShares: null,
            exercisePrice: 130m
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(3_659_027m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-183_640)]
    public void Evaluate_OneForOneDerivativeWithoutPositiveShares_IsValidAsFiled(long shares)
    {
        var result = EvaluateDerivative(
            reportedPrice: 3_659_027m,
            shares: shares,
            bar: new DailyBarContext { Close = 108m },
            underlyingShares: shares,
            exercisePrice: 130m
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(3_659_027m);
    }

    [Fact]
    public void Evaluate_OneForOneDerivativeWithNoBar_IsValidAsFiledNotPending()
    {
        // A delisted underlying has no close; the row must keep today's verdict, not go pending.
        var result = EvaluateDerivative(
            reportedPrice: 3_659_027m,
            shares: 183_640,
            bar: null,
            underlyingShares: 183_640,
            exercisePrice: 130m
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(3_659_027m);
    }

    [Fact]
    public void Evaluate_OneForOneDerivativeWithZeroClose_IsValidAsFiledNotPending()
    {
        var result = EvaluateDerivative(
            reportedPrice: 3_659_027m,
            shares: 183_640,
            bar: new DailyBarContext { Close = 0m },
            underlyingShares: 183_640,
            exercisePrice: 130m
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(3_659_027m);
    }

    [Fact]
    public void Evaluate_OneForOneDerivativeWithAmbiguousSplitBasis_IsValidAsFiledNotPending()
    {
        var result = EvaluateDerivative(
            reportedPrice: 3_659_027m,
            shares: 183_640,
            bar: new DailyBarContext { Close = 108m, SplitBasisAmbiguous = true },
            underlyingShares: 183_640,
            exercisePrice: 130m
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(3_659_027m);
    }

    [Fact]
    public void Evaluate_DeepInTheMoneyPutWithModestPremium_IsValidAsFiled()
    {
        var result = EvaluateDerivative(
            reportedPrice: 195m,
            shares: 1_000,
            bar: new DailyBarContext { Close = 100m },
            underlyingShares: 1_000,
            exercisePrice: 300m,
            securityTitle: "Put option (right to sell)"
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(195m);
    }

    [Fact]
    public void Evaluate_PutPremiumAboveTenTimesTheCloseButUnderTheStrike_IsValidAsFiled()
    {
        // 1,950 / 10 = 195 clears the $100 close but not the $300 strike, so the strike is what
        // keeps a real put premium from being "repaired".
        var result = EvaluateDerivative(
            reportedPrice: 1_950m,
            shares: 1_000,
            bar: new DailyBarContext { Close = 100m },
            underlyingShares: 1_000,
            exercisePrice: 300m,
            securityTitle: "Put option (right to sell)"
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(1_950m);
    }

    [Fact]
    public void Evaluate_OneForOneDerivativeAtExactlyTenTimesTheCeiling_IsValidAsFiled()
    {
        var result = EvaluateDerivative(
            reportedPrice: 1_000m,
            shares: 1_000,
            bar: new DailyBarContext { Close = 100m },
            underlyingShares: 1_000,
            exercisePrice: 1m
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(1_000m);
    }

    [Fact]
    public void Evaluate_OneForOneDerivativeWithoutStrikeValue_IsValidAsFiled()
    {
        // An option expiring underwater, its footnoted $50 strike filed as the price while the
        // share trades at $3: with no stated strike there is no ceiling to repair against.
        var result = EvaluateDerivative(
            reportedPrice: 50m,
            shares: 10_000,
            bar: new DailyBarContext { Close = 3m },
            underlyingShares: 10_000,
            exercisePrice: null
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(50m);
    }

    [Fact]
    public void Evaluate_OneForOneCandidateExactlyAtTheCeiling_IsRepaired()
    {
        var result = EvaluateDerivative(
            reportedPrice: 100_000m,
            shares: 1_000,
            bar: new DailyBarContext { Close = 100m },
            underlyingShares: 1_000,
            exercisePrice: 1m
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.EffectivePrice.Should().Be(100m);
    }

    [Fact]
    public void Evaluate_PreSplitOneForOnePremium_IsCheckedOnTheAsFiledBasis()
    {
        // The stored close is post-10:1 ($10); on the as-filed basis the share was $100, so a
        // $500 premium (50 after the 10x cap) is real, not a total.
        var result = EvaluateDerivative(
            reportedPrice: 500m,
            shares: 1_000,
            bar: new DailyBarContext { Close = 10m, SplitFactorToPresent = 10m },
            underlyingShares: 1_000,
            exercisePrice: 1m
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(500m);
    }

    [Fact]
    public void Evaluate_OneForOneDerivativeWhoseCandidateAlsoExceedsTheCeiling_IsInvalidUnrepaired()
    {
        // 1,000,000 / 10 units = 100,000 per unit, still far above a $50 share and a $60 strike.
        var result = EvaluateDerivative(
            reportedPrice: 1_000_000m,
            shares: 10,
            bar: new DailyBarContext { Close = 50m },
            underlyingShares: 10,
            exercisePrice: 60m
        );

        result.IsPriceValid.Should().BeFalse();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(1_000_000m);
    }

    [Fact]
    public void Evaluate_UnknownKindWithDerivativeTitleAndEvidence_TakesTheDerivativeRepair()
    {
        var result = _validator.Evaluate(
            reportedPrice: 3_659_027m,
            shares: 183_640,
            kind: InsiderSecurityKind.Unknown,
            securityTitle: "Call Option",
            bar: new DailyBarContext { Close = 108m },
            underlyingShares: 183_640,
            exercisePrice: 130m
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.EffectivePrice.Should().Be(3_659_027m / 183_640m);
    }

    [Fact]
    public void Evaluate_NonDerivativeRowWithEvidence_IgnoresTheEvidence()
    {
        // The evidence only feeds the derivative rule; a common-stock row keeps its own path.
        var result = _validator.Evaluate(
            reportedPrice: 55m,
            shares: 1_000,
            kind: InsiderSecurityKind.NonDerivative,
            securityTitle: "Common Stock",
            bar: null,
            underlyingShares: 1_000,
            exercisePrice: 130m
        );

        result.IsPriceValid.Should().BeNull();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(55m);
    }
}
