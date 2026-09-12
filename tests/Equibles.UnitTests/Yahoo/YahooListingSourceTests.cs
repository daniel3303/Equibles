using Equibles.Integrations.Yahoo.Models;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

public class YahooListingSourceTests
{
    [Theory]
    [InlineData("XLIS")]
    [InlineData("ENXL")]
    [InlineData("ALXL")]
    public void LisbonCandidateRequiresMatchingChartMetadata(string mic)
    {
        var target = new PriceSeriesTarget(
            "ALTR",
            Guid.NewGuid(),
            Guid.NewGuid(),
            false,
            MarketCountryCode: "PT",
            MarketIdentifierCode: mic,
            Isin: "PTALT0AE0002"
        );
        target.ProviderSymbol.Should().Be("ALTR.LS");
        YahooListingSource
            .MatchesChart(
                target,
                new YahooChartSourceIdentity
                {
                    Symbol = "ALTR.LS",
                    Currency = "EUR",
                    ExchangeCode = "LIS",
                    InstrumentType = "EQUITY",
                    ExchangeTimeZone = "Europe/Lisbon",
                }
            )
            .Should()
            .BeTrue();
        YahooListingSource.MatchesChart(target, null).Should().BeFalse();
    }

    [Theory]
    [InlineData("PT", "XLON", "PTALT0AE0002")]
    [InlineData("GB", "XLIS", "PTALT0AE0002")]
    [InlineData(null, "XLIS", "PTALT0AE0002")]
    [InlineData("PT", "XLIS", null)]
    public void UnsupportedIdentityCannotBecomeAProviderSymbol(
        string country,
        string mic,
        string isin
    )
    {
        var target = new PriceSeriesTarget(
            "SAME",
            Guid.NewGuid(),
            Guid.NewGuid(),
            false,
            MarketCountryCode: country,
            MarketIdentifierCode: mic,
            Isin: isin
        );
        target.ProviderSymbol.Should().BeNull();
    }
}
