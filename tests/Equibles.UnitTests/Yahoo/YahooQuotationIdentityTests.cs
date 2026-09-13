using Equibles.Integrations.Yahoo.Models;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

public class YahooQuotationIdentityTests
{
    [Theory]
    [InlineData("AAPL", "USD", "NMS", "America/New_York", true)]
    [InlineData("aapl", "USD", "NMS", "America/New_York", true)]
    [InlineData("AAPL.L", "USD", "LSE", "Europe/London", false)]
    [InlineData("AAPL", "GBp", "LSE", "Europe/London", false)]
    [InlineData("AAPL", "EUR", "LIS", "Europe/Lisbon", false)]
    [InlineData("AAPL", null, "NMS", "America/New_York", false)]
    [InlineData(null, "USD", "NMS", "America/New_York", false)]
    [InlineData("AAPL", "USD", null, "America/New_York", false)]
    [InlineData("AAPL", "USD", "NMS", null, false)]
    [InlineData(" AAPL ", "USD", "NMS", "America/New_York", false)]
    public void Denomination_RequiresExactReturnedSymbolAndExplicitDollarUnits(
        string symbol,
        string currency,
        string exchange,
        string timezone,
        bool expected
    )
    {
        YahooQuotationIdentity
            .HasUsDollarEvidence(
                "AAPL",
                new YahooChartSourceIdentity
                {
                    Symbol = symbol,
                    Currency = currency,
                    ExchangeCode = exchange,
                    ExchangeTimeZone = timezone,
                }
            )
            .Should()
            .Be(expected);
    }

    [Fact]
    public void MissingMetadataAndDifferentSeparator_NeverEstablishAQuotationBasis()
    {
        YahooQuotationIdentity.HasUsDollarEvidence("AAPL", null).Should().BeFalse();
        YahooQuotationIdentity
            .HasUsDollarEvidence(
                "AAPL-L",
                new YahooChartSourceIdentity
                {
                    Symbol = "AAPL.L",
                    Currency = "USD",
                    ExchangeCode = "LSE",
                    ExchangeTimeZone = "Europe/London",
                }
            )
            .Should()
            .BeFalse();
    }
}
