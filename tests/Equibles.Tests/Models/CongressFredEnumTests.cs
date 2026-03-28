using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Equibles.Congress.Data.Models;
using Equibles.Core.Extensions;
using Equibles.Fred.Data.Models;

namespace Equibles.Tests.Models;

public class CongressFredEnumTests {
    [Fact]
    public void CongressPosition_Representative_NameForHumans_Returns_Representative() {
        CongressPosition.Representative.NameForHumans().Should().Be("Representative");
    }

    [Fact]
    public void CongressPosition_Senator_NameForHumans_Returns_Senator() {
        CongressPosition.Senator.NameForHumans().Should().Be("Senator");
    }

    [Fact]
    public void CongressTransactionType_Purchase_NameForHumans_Returns_Purchase() {
        CongressTransactionType.Purchase.NameForHumans().Should().Be("Purchase");
    }

    [Fact]
    public void CongressTransactionType_Sale_NameForHumans_Returns_Sale() {
        CongressTransactionType.Sale.NameForHumans().Should().Be("Sale");
    }

    [Theory]
    [InlineData(FredSeriesCategory.InterestRates, "Interest Rates")]
    [InlineData(FredSeriesCategory.YieldSpreads, "Yield Spreads")]
    [InlineData(FredSeriesCategory.CorporateBondSpreads, "Corporate Bond Spreads")]
    [InlineData(FredSeriesCategory.Inflation, "Inflation")]
    [InlineData(FredSeriesCategory.Employment, "Employment")]
    [InlineData(FredSeriesCategory.GdpAndOutput, "GDP & Output")]
    [InlineData(FredSeriesCategory.MoneySupply, "Money Supply")]
    [InlineData(FredSeriesCategory.Sentiment, "Sentiment")]
    [InlineData(FredSeriesCategory.Housing, "Housing")]
    [InlineData(FredSeriesCategory.ExchangeRates, "Exchange Rates")]
    [InlineData(FredSeriesCategory.Market, "Market")]
    public void FredSeriesCategory_NameForHumans_Returns_DisplayName(
        FredSeriesCategory category, string expected) {
        category.NameForHumans().Should().Be(expected);
    }

    [Fact]
    public void All_FredSeriesCategory_Values_Have_Display_Attribute() {
        var values = Enum.GetValues<FredSeriesCategory>();

        values.Should().HaveCount(11);

        foreach (var value in values) {
            var member = typeof(FredSeriesCategory).GetMember(value.ToString()).First();
            member.GetCustomAttribute<DisplayAttribute>().Should().NotBeNull(
                because: $"{value} should have a [Display] attribute");
        }
    }

    [Fact]
    public void All_CongressPosition_Values_Have_Display_Attribute() {
        var values = Enum.GetValues<CongressPosition>();

        values.Should().HaveCount(2);

        foreach (var value in values) {
            var member = typeof(CongressPosition).GetMember(value.ToString()).First();
            member.GetCustomAttribute<DisplayAttribute>().Should().NotBeNull(
                because: $"{value} should have a [Display] attribute");
        }
    }

    [Fact]
    public void All_CongressTransactionType_Values_Have_Display_Attribute() {
        var values = Enum.GetValues<CongressTransactionType>();

        values.Should().HaveCount(2);

        foreach (var value in values) {
            var member = typeof(CongressTransactionType).GetMember(value.ToString()).First();
            member.GetCustomAttribute<DisplayAttribute>().Should().NotBeNull(
                because: $"{value} should have a [Display] attribute");
        }
    }
}
