using System.Net;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Yahoo;

public class YahooFinanceClientGetHistoricalPricesNullOhlcHoleTests
{
    // Contract: Yahoo occasionally returns a null hole in an OHLC column on a
    // holiday/early-close row. The documented behaviour is to skip such an
    // incomplete row "like a holiday gap rather than emit an OHLC-impossible
    // bar (e.g. High=0 while Close>0)". Here bar 1 (in-window) has low=null, so
    // only the complete bar 0 must survive — never a fabricated 2024-01-03 row.
    [Fact]
    public async Task GetHistoricalPrices_RowWithNullOhlcField_SkipsRowNotEmitsImpossibleBar()
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
                          "open": [100.0, 200.0],
                          "high": [101.0, 201.0],
                          "low": [99.0, null],
                          "close": [100.5, 200.5],
                          "volume": [555, 666]
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
        price.Low.Should().Be(99m);
        price.Close.Should().Be(100.5m);
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
