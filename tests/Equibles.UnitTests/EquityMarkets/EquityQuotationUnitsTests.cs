using Equibles.EquityMarkets.Data.Catalog;

namespace Equibles.UnitTests.EquityMarkets;

public class EquityQuotationUnitsTests
{
    [Theory]
    [InlineData("EUR", "EUR", 1)]
    [InlineData("NOK", "NOK", 1)]
    [InlineData("GBP", "GBP", 1)]
    [InlineData("GBp", "GBP", 0.01)]
    [InlineData("GBX", "GBP", 0.01)]
    public void KnownTokens_ResolveToIsoCurrencyAndMajorUnitMultiplier(
        string token,
        string currency,
        decimal multiplier
    )
    {
        EquityQuotationUnits.TryResolve(token, out var resolved, out var scale).Should().BeTrue();
        resolved.Should().Be(currency);
        scale.Should().Be(multiplier);
        EquityQuotationUnits.Matches(token, currency, multiplier).Should().BeTrue();
    }

    [Theory]
    [InlineData("gbp")]
    [InlineData("ZAc")]
    [InlineData("JPY")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownTokens_NeverGuessAScale(string token)
    {
        EquityQuotationUnits.TryResolve(token, out var currency, out _).Should().BeFalse();
        currency.Should().BeNull();
        EquityQuotationUnits.Matches(token, "GBP", 1m).Should().BeFalse();
    }

    [Fact]
    public void Matches_RequiresBothTheCurrencyAndTheMultiplierTheListingStates()
    {
        EquityQuotationUnits.Matches("GBp", "GBP", 1m).Should().BeFalse();
        EquityQuotationUnits.Matches("GBP", "GBP", 0.01m).Should().BeFalse();
        EquityQuotationUnits.Matches("EUR", "USD", 1m).Should().BeFalse();
        EquityQuotationUnits.Matches("EUR", "EUR", null).Should().BeFalse();
    }
}
