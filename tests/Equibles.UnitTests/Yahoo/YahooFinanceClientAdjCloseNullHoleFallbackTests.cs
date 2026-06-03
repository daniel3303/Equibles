using System.Net;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Yahoo;

public class YahooFinanceClientAdjCloseNullHoleFallbackTests
{
    // Contract: an adjclose null hole on a holiday-edge row means "unavailable" — the bar must
    // fall back to the day's Close, never 0. The OHLC-null-hole pin skips the row and never
    // asserts AdjustedClose, so the documented "never 0" fallback is otherwise unverified.
    [Fact]
    public async Task GetHistoricalPrices_AdjCloseNullHoleWithCompleteOhlc_FallsBackToCloseNotZero()
    {
        // UTC exchange (gmtoffset 0): 1704153600 -> 2024-01-02. Complete OHLC, adjclose hole.
        const string cassette = """
            {
              "chart": {
                "result": [
                  {
                    "meta": { "gmtoffset": 0, "exchangeTimezoneName": "UTC" },
                    "timestamp": [1704153600],
                    "indicators": {
                      "quote": [
                        {
                          "open": [100.0],
                          "high": [101.0],
                          "low": [99.0],
                          "close": [100.5],
                          "volume": [555]
                        }
                      ],
                      "adjclose": [ { "adjclose": [null] } ]
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
            new DateOnly(2024, 1, 2)
        );

        prices.Should().ContainSingle();
        prices[0].AdjustedClose.Should().Be(100.5m);
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
