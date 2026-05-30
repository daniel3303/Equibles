using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderTransactionPriceValidatorTests
{
    private readonly InsiderTransactionPriceValidator _validator = new();

    // Form 3 holdings and post-transaction-only rows pass through the parser
    // with PricePerShare = 0 by design. They aren't a real per-share price
    // to validate; dropping them off the dashboard would be wrong.
    [Fact]
    public void IsPlausible_ZeroPriceWithNullClose_ReturnsTrue()
    {
        var result = _validator.IsPlausible(0m, "Common Stock", null);

        result.Should().BeTrue();
    }

    // Derivative rows carry the option / warrant price, not the underlying
    // close — strike prices well above market are normal. The 10× ceiling
    // doesn't apply, otherwise legitimate deep-OTM derivative grants would
    // be hidden from the dashboard.
    [Theory]
    [InlineData("Stock Option (Right to Buy)")]
    [InlineData("Warrant")]
    [InlineData("Convertible Preferred")]
    public void IsPlausible_DerivativeSecurityTitle_ReturnsTrue(string securityTitle)
    {
        // 1000× a $50 close — would be rejected if treated as common stock.
        var result = _validator.IsPlausible(50_000m, securityTitle, 50m);

        result.Should().BeTrue();
    }

    // No Yahoo data on file (delisted ticker, brand-new IPO not yet ingested,
    // foreign listing). Can't validate — must not flip valid rows to invalid
    // just because the price feed hasn't caught up.
    [Fact]
    public void IsPlausible_CommonStockWithNullClose_ReturnsTrue()
    {
        var result = _validator.IsPlausible(0.24m, "Common Stock", null);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPlausible_PriceUnderTenTimesClose_ReturnsTrue()
    {
        // Real-world spread on a single execution: typically <2× the close.
        // 5× the close is comfortably under the 10× ceiling.
        var result = _validator.IsPlausible(1.20m, "Common Stock", 0.24m);

        result.Should().BeTrue();
    }

    // The bug this whole feature exists to catch — Synchron/REEMF reported
    // PricePerShare = $24,035,774.40 on a $0.24 stock, because the filer
    // typed the total transaction value into the per-share field.
    [Fact]
    public void IsPlausible_TotalValueInPriceField_ReturnsFalse()
    {
        var result = _validator.IsPlausible(
            pricePerShare: 24_035_774.40m,
            securityTitle: "Common Stock",
            unadjustedClose: 0.24m
        );

        result.Should().BeFalse();
    }

    // Magnetar/CRWV: stored per-share values in the $11M range against an
    // actual close in the $20s. Same failure mode, different filer.
    [Fact]
    public void IsPlausible_MagnetarStylePrice_ReturnsFalse()
    {
        var result = _validator.IsPlausible(
            pricePerShare: 11_000_689.50m,
            securityTitle: "Common Stock",
            unadjustedClose: 20.50m
        );

        result.Should().BeFalse();
    }

    // The boundary itself — exactly 10× must pass (≤, not <), so a one-cent
    // tweak in the production constant changes behavior here and is caught.
    [Fact]
    public void IsPlausible_PriceAtExactlyTenTimesClose_ReturnsTrue()
    {
        var result = _validator.IsPlausible(100m, "Common Stock", 10m);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPlausible_PriceJustAboveTenTimesClose_ReturnsFalse()
    {
        var result = _validator.IsPlausible(100.01m, "Common Stock", 10m);

        result.Should().BeFalse();
    }

    // Negative prices aren't the bug we're hunting — could surface from a
    // future amender's bizarre entry, but rejecting them would hide the row
    // and obscure the real data quality issue.
    [Fact]
    public void IsPlausible_NegativePrice_ReturnsTrue()
    {
        var result = _validator.IsPlausible(-5m, "Common Stock", 10m);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPlausible_ZeroOrNegativeClose_ReturnsTrue()
    {
        var result = _validator.IsPlausible(100m, "Common Stock", 0m);

        result.Should().BeTrue();
    }

    // Evaluate — full tri-state + repair. Unlike IsPlausible, a missing close
    // is *pending* (null), not an automatic pass: the row gets re-checked once
    // the close lands instead of being silently accepted.
    [Fact]
    public void Evaluate_CommonStockWithNullClose_IsPending()
    {
        var result = _validator.Evaluate(
            0.24m,
            shares: 1000,
            "Common Stock",
            unadjustedClose: null
        );

        result.IsPriceValid.Should().BeNull();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(0.24m);
    }

    // The bug this feature exists to fix — filer typed the total transaction
    // value ($24,035,774.40) into the per-share field. Divide by the share
    // count to recover the unit price, and mark it valid.
    [Fact]
    public void Evaluate_TotalValueInPriceField_RepairsByDividingByShares()
    {
        var result = _validator.Evaluate(
            reportedPrice: 24_035_774.40m,
            shares: 100_149_893, // 24,035,774.40 / 0.24
            securityTitle: "Common Stock",
            unadjustedClose: 0.24m
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.EffectivePrice.Should().Be(24_035_774.40m / 100_149_893);
    }

    // Implausible but no share count to divide by — can't repair, so it's
    // positively rejected (the only thing dashboards hide).
    [Fact]
    public void Evaluate_ImplausibleWithZeroShares_IsInvalidNotRepaired()
    {
        var result = _validator.Evaluate(50_000m, shares: 0, "Common Stock", unadjustedClose: 50m);

        result.IsPriceValid.Should().BeFalse();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(50_000m);
    }

    [Fact]
    public void Evaluate_PlausiblePrice_IsValidUnchanged()
    {
        var result = _validator.Evaluate(55m, shares: 1000, "Common Stock", unadjustedClose: 50m);

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(55m);
    }

    // Derivatives carry the instrument's own price; never repaired, valid even
    // far above the underlying close and even with no close on file.
    [Fact]
    public void Evaluate_DerivativeWayAboveClose_IsValidUnchanged()
    {
        var result = _validator.Evaluate(
            50_000m,
            shares: 1000,
            "Stock Option (Right to Buy)",
            unadjustedClose: 50m
        );

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(50_000m);
    }

    [Fact]
    public void Evaluate_ZeroPrice_IsValidWithoutClose()
    {
        var result = _validator.Evaluate(0m, shares: 0, "Common Stock", unadjustedClose: null);

        result.IsPriceValid.Should().BeTrue();
        result.WasRepaired.Should().BeFalse();
        result.EffectivePrice.Should().Be(0m);
    }
}
