using Equibles.Congress.Data.Models;
using Equibles.Core.Extensions;
using Equibles.Fred.Data.Models;
using Equibles.InsiderTrading.Data.Models;

namespace Equibles.UnitTests.Core;

public class EnumExtensionsTests {
    [Fact]
    public void NameForHumans_WhenDisplayNameMatchesValueName_ReturnsDisplayName() {
        TransactionCode.Purchase.NameForHumans().Should().Be("Purchase");
    }

    [Fact]
    public void NameForHumans_WhenDisplayNameDiffersFromValueName_ReturnsDisplayName() {
        TransactionCode.TaxPayment.NameForHumans().Should().Be("Tax Payment");
    }

    [Theory]
    [InlineData(TransactionCode.Purchase, "Purchase")]
    [InlineData(TransactionCode.Sale, "Sale")]
    [InlineData(TransactionCode.TaxPayment, "Tax Payment")]
    public void NameForHumans_TransactionCode_ReturnsExpectedDisplayName(
        TransactionCode value, string expected) {
        value.NameForHumans().Should().Be(expected);
    }

    [Theory]
    [InlineData(FredSeriesCategory.InterestRates, "Interest Rates")]
    [InlineData(FredSeriesCategory.GdpAndOutput, "GDP & Output")]
    [InlineData(FredSeriesCategory.CorporateBondSpreads, "Corporate Bond Spreads")]
    [InlineData(FredSeriesCategory.ExchangeRates, "Exchange Rates")]
    public void NameForHumans_FredSeriesCategory_ReturnsMultiWordDisplayName(
        FredSeriesCategory value, string expected) {
        value.NameForHumans().Should().Be(expected);
    }

    [Theory]
    [InlineData(CongressPosition.Representative, "Representative")]
    [InlineData(CongressPosition.Senator, "Senator")]
    public void NameForHumans_CongressPosition_ReturnsExpectedDisplayName(
        CongressPosition value, string expected) {
        value.NameForHumans().Should().Be(expected);
    }

    [Fact]
    public void NameForHumans_EnumValueWithoutDisplayAttribute_FallsBackToToString() {
        // Project standards require [Display(Name = "...")] on every enum
        // value (see dotnet-standards), so the fallback only fires for
        // legacy enums or new ones added without the convention. Every
        // existing test in this file pre-supposes [Display] is present,
        // so the `?.GetName() ?? enumValue.ToString()` fallback in
        // NameForHumans isn't exercised. Pin the fallback on a locally
        // defined enum that intentionally omits [Display] so a refactor
        // that drops the fallback (e.g. relying on Display always being
        // present) surfaces here rather than NRE-ing on a missed
        // annotation in production.
        WithoutDisplay.SomeValue.NameForHumans().Should().Be("SomeValue");
    }

    private enum WithoutDisplay {
        SomeValue,
    }
}
