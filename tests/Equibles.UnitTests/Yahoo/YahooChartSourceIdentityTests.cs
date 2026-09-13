using Equibles.Integrations.Yahoo;

namespace Equibles.UnitTests.Yahoo;

public class YahooChartSourceIdentityTests
{
    [Theory]
    [InlineData("altri", "ALTR.LS", "EUR", "LIS", "Lisbon", "Europe/Lisbon")]
    [InlineData("vod", "VOD.L", "GBp", "LSE", "LSE", "Europe/London")]
    [InlineData("aapl", "AAPL", "USD", "NMS", "NasdaqGS", "America/New_York")]
    public async Task CapturedChart_PreservesProviderIdentityAndDenomination(
        string fixture,
        string symbol,
        string currency,
        string exchangeCode,
        string exchangeName,
        string timeZone
    )
    {
        var body = await File.ReadAllTextAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Yahoo",
                "ChartIdentity",
                fixture + ".json"
            )
        );
        using var http = new HttpClient(new YahooChartIdentityHandler(body));
        var chart = await new YahooChartIdentityClient(http).GetChart(
            symbol,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 12)
        );
        chart.SourceIdentity.Symbol.Should().Be(symbol);
        chart.SourceIdentity.Currency.Should().Be(currency);
        chart.SourceIdentity.ExchangeCode.Should().Be(exchangeCode);
        chart.SourceIdentity.ExchangeName.Should().Be(exchangeName);
        chart.SourceIdentity.ExchangeTimeZone.Should().Be(timeZone);
        chart.SourceIdentity.InstrumentType.Should().Be("EQUITY");
        chart.Prices.Should().NotBeEmpty();
    }

    [Fact]
    public async Task MissingMetadata_DoesNotInventIdentityFromTheRequestedTicker()
    {
        using var http = new HttpClient(
            new YahooChartIdentityHandler("""{"chart":{"result":[{}]}}""")
        );
        var chart = await new YahooChartIdentityClient(http).GetChart(
            "AAPL",
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 12)
        );
        chart.SourceIdentity.Should().BeNull();
    }

    [Fact]
    public async Task DifferentReturnedSymbol_RemainsVisibleForTheCallerToReject()
    {
        using var http = new HttpClient(
            new YahooChartIdentityHandler(
                """{"chart":{"result":[{"meta":{"symbol":"OTHER.L"}}]}}"""
            )
        );
        var chart = await new YahooChartIdentityClient(http).GetChart(
            "AAPL",
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 12)
        );
        chart.SourceIdentity.Symbol.Should().Be("OTHER.L");
        chart.SourceIdentity.Currency.Should().BeNull();
        chart.SourceIdentity.ExchangeCode.Should().BeNull();
        chart.SourceIdentity.InstrumentType.Should().BeNull();
    }
}
