using System.Net;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Yahoo;

// Contract: the chart endpoint fetched with events=div|split carries a
// chart.result[0].events.dividends object keyed by epoch-string. GetChart must
// parse every in-window dividend — dated on the exchange-local calendar, with
// the declared cash amount per share — alongside the price bars, drop
// non-positive amounts as unusable, and trim events outside the requested
// window. Fixture mirrors real AAPL-style data: $0.24 ex 2024-02-09 and $0.25
// ex 2024-05-09 in-window (gmtoffset 0), one 2023 dividend outside the window,
// and one zero-amount junk row, plus two price bars.
public class YahooDividendParsingTests
{
    private const string DividendCassette = """
        {
          "chart": {
            "result": [
              {
                "meta": { "gmtoffset": 0, "exchangeTimezoneName": "UTC" },
                "timestamp": [1704153600, 1704240000],
                "events": {
                  "dividends": {
                    "1707486600": { "date": 1707486600, "amount": 0.24 },
                    "1715261400": { "date": 1715261400, "amount": 0.25 },
                    "1691760600": { "date": 1691760600, "amount": 0.23 },
                    "1723123800": { "date": 1723123800, "amount": 0.0 }
                  }
                },
                "indicators": {
                  "quote": [
                    {
                      "open": [184.0, 183.0],
                      "high": [186.0, 185.0],
                      "low": [183.0, 182.0],
                      "close": [185.5, 184.5],
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

    private static readonly DateOnly WindowStart = new(2024, 1, 1);
    private static readonly DateOnly WindowEnd = new(2024, 12, 31);

    [Fact]
    public async Task GetChart_DividendEventsInWindow_ParsesBothWithCorrectDatesAndAmounts()
    {
        var client = new SessionStubbedYahooClient(
            new HttpClient(new CassetteHandler(DividendCassette))
        );

        var chart = await client.GetChart("AAPL", WindowStart, WindowEnd);

        chart.Dividends.Should().HaveCount(2);

        var february = chart.Dividends.Single(d => d.Date == new DateOnly(2024, 2, 9));
        february.Amount.Should().Be(0.24m);

        var may = chart.Dividends.Single(d => d.Date == new DateOnly(2024, 5, 9));
        may.Amount.Should().Be(0.25m);
    }

    [Fact]
    public async Task GetChart_OutOfWindowAndZeroAmountDividends_AreDropped()
    {
        var client = new SessionStubbedYahooClient(
            new HttpClient(new CassetteHandler(DividendCassette))
        );

        var chart = await client.GetChart("AAPL", WindowStart, WindowEnd);

        chart.Dividends.Should().NotContain(d => d.Date.Year == 2023);
        chart.Dividends.Should().NotContain(d => d.Amount <= 0);
    }

    [Fact]
    public async Task GetChart_SameResponse_StillReturnsPriceBarsAndNoSplits()
    {
        var client = new SessionStubbedYahooClient(
            new HttpClient(new CassetteHandler(DividendCassette))
        );

        var chart = await client.GetChart("AAPL", WindowStart, WindowEnd);

        chart.Prices.Should().HaveCount(2);
        chart.Prices.Should().Contain(p => p.Date == new DateOnly(2024, 1, 2) && p.Close == 185.5m);
        chart.Prices.Should().Contain(p => p.Date == new DateOnly(2024, 1, 3) && p.Close == 184.5m);
        chart.Splits.Should().BeEmpty();
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
