using System.Net;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Yahoo;

public class YahooFinanceClientGetHistoricalPricesZeroPriceTests
{
    // Contract: Yahoo returns a zeroed OHLC quartet (open/high/low/close all 0) for a
    // delisted or trading-halted ticker, sometimes still carrying non-zero volume. A listed
    // equity never trades at $0, so such a row is not a tradeable price and must be skipped
    // like a holiday gap rather than emitted as an impossible bar (Close=0 on a day with
    // Volume>0), which would corrupt market cap and price-derived rankings. Here bar 1 is
    // all-zero with volume 1396, so only the valid bar 0 must survive.
    [Fact]
    public async Task GetHistoricalPrices_ZeroOhlcRowWithVolume_SkipsRowNotEmitsImpossibleBar()
    {
        // UTC exchange (gmtoffset 0) so each midnight timestamp maps to its own
        // calendar date: 1704153600 -> 2024-01-02, 1704240000 -> 2024-01-03.
        const string cassette = """
            {
              "chart": {
                "result": [
                  {
                    "meta": { "gmtoffset": 0, "exchangeTimezoneName": "UTC" },
                    "timestamp": [1704153600, 1704240000],
                    "indicators": {
                      "quote": [
                        {
                          "open": [100.0, 0.0],
                          "high": [101.0, 0.0],
                          "low": [99.0, 0.0],
                          "close": [100.5, 0.0],
                          "volume": [555, 1396]
                        }
                      ]
                    }
                  }
                ],
                "error": null
              }
            }
            """;

        var client = new SessionStubbedYahooClient(new HttpClient(new CassetteHandler(cassette)));

        var prices = await client.GetHistoricalPrices(
            "TEST",
            new DateOnly(2024, 1, 2),
            new DateOnly(2024, 1, 3)
        );

        prices.Should().ContainSingle();
        var price = prices[0];
        price.Date.Should().Be(new DateOnly(2024, 1, 2));
        price.Close.Should().Be(100.5m);
        prices.Should().NotContain(p => p.Date == new DateOnly(2024, 1, 3));
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
