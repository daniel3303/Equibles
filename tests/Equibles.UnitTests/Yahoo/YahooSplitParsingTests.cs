using System.Net;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Yahoo;

// Contract: the chart endpoint fetched with events=split carries a
// chart.result[0].events.splits object keyed by epoch-string. GetChart must
// parse every in-window split — dated on the exchange-local calendar, ratio
// Numerator:Denominator — alongside the price bars, while GetHistoricalPrices
// keeps returning only the prices. Fixture mirrors real NVDA data: 4:1 on
// 2021-07-20 and 10:1 on 2024-06-10 (gmtoffset 0 so each pre-market epoch maps
// to its own UTC calendar date), plus two price bars.
public class YahooSplitParsingTests
{
    private const string NvdaSplitCassette = """
        {
          "chart": {
            "result": [
              {
                "meta": { "gmtoffset": 0, "exchangeTimezoneName": "UTC" },
                "timestamp": [1719532800, 1719792000],
                "events": {
                  "splits": {
                    "1626787800": { "date": 1626787800, "numerator": 4.0, "denominator": 1.0, "splitRatio": "4:1" },
                    "1718022600": { "date": 1718022600, "numerator": 10.0, "denominator": 1.0, "splitRatio": "10:1" }
                  }
                },
                "indicators": {
                  "quote": [
                    {
                      "open": [120.0, 122.0],
                      "high": [121.0, 123.0],
                      "low": [119.0, 121.0],
                      "close": [120.5, 122.5],
                      "volume": [1000, 2000]
                    }
                  ]
                }
              }
            ],
            "error": null
          }
        }
        """;

    private static readonly DateOnly WindowStart = new(2021, 7, 1);
    private static readonly DateOnly WindowEnd = new(2024, 7, 1);

    [Fact]
    public async Task GetChart_SplitEventsInWindow_ParsesBothWithCorrectDatesAndRatios()
    {
        var client = new SessionStubbedYahooClient(
            new HttpClient(new CassetteHandler(NvdaSplitCassette))
        );

        var chart = await client.GetChart("NVDA", WindowStart, WindowEnd);

        chart.Splits.Should().HaveCount(2);

        var forward = chart.Splits.Single(s => s.Date == new DateOnly(2021, 7, 20));
        forward.Numerator.Should().Be(4m);
        forward.Denominator.Should().Be(1m);

        var tenForOne = chart.Splits.Single(s => s.Date == new DateOnly(2024, 6, 10));
        tenForOne.Numerator.Should().Be(10m);
        tenForOne.Denominator.Should().Be(1m);
    }

    [Fact]
    public async Task GetChart_SameResponse_StillReturnsPriceBars()
    {
        var client = new SessionStubbedYahooClient(
            new HttpClient(new CassetteHandler(NvdaSplitCassette))
        );

        var chart = await client.GetChart("NVDA", WindowStart, WindowEnd);

        chart.Prices.Should().HaveCount(2);
        chart
            .Prices.Should()
            .Contain(p => p.Date == new DateOnly(2024, 6, 28) && p.Close == 120.5m);
        chart.Prices.Should().Contain(p => p.Date == new DateOnly(2024, 7, 1) && p.Close == 122.5m);
    }

    [Fact]
    public async Task GetHistoricalPrices_SplitCassette_StillReturnsOnlyPrices()
    {
        var client = new SessionStubbedYahooClient(
            new HttpClient(new CassetteHandler(NvdaSplitCassette))
        );

        var prices = await client.GetHistoricalPrices("NVDA", WindowStart, WindowEnd);

        prices.Should().HaveCount(2);
        prices
            .Select(p => p.Date)
            .Should()
            .BeEquivalentTo([new DateOnly(2024, 6, 28), new DateOnly(2024, 7, 1)]);
    }

    // Overrides the documented protected-virtual session seam so the data fetch
    // skips the live cookie/crumb bootstrap and runs entirely against the stub.
    private sealed class SessionStubbedYahooClient(HttpClient httpClient)
        : YahooFinanceClient(httpClient, NullLogger<YahooFinanceClient>.Instance)
    {
        protected override Task<(string Crumb, string CookieHeader)> EnsureSession() =>
            Task.FromResult<(string Crumb, string CookieHeader)>(("test-crumb", "A=1"));
    }

    private sealed class CassetteHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
            );
    }
}
