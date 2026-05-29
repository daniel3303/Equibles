using System.Net;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Yahoo;

public class YahooFinanceClientGetHistoricalPricesTests
{
    // Pins the 0%-covered end-to-end parse path: the offset-aware window trim
    // (Yahoo stamps bars in exchange-local time, so an east-of-UTC bar for a
    // requested day sits on the previous UTC calendar date), plus 4-dp OHLC
    // rounding and the adjclose -> close fallback when no adjclose array exists.
    [Fact]
    public async Task GetHistoricalPrices_EastOfUtcBars_TrimsToExchangeLocalWindowAndParsesOhlc()
    {
        // Tokyo offset (+9h = 32400s). Bar 0's instant (2024-01-02 15:00 UTC) maps
        // to local 2024-01-03; bar 1's (2024-01-03 15:00 UTC) maps to local 2024-01-04.
        // Requesting only 2024-01-04 must keep bar 1 and drop bar 0 — without the
        // offset shift bar 1 would land on 2024-01-03 and the result would be empty.
        const string cassette = """
            {
              "chart": {
                "result": [
                  {
                    "meta": { "gmtoffset": 32400, "exchangeTimezoneName": "Asia/Tokyo" },
                    "timestamp": [1704207600, 1704294000],
                    "indicators": {
                      "quote": [
                        {
                          "open": [100.0, 185.1234],
                          "high": [101.0, 187.6543],
                          "low": [99.0, 184.0009],
                          "close": [100.5, 186.789067],
                          "volume": [555, 12345678]
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
            "TEST.T",
            new DateOnly(2024, 1, 4),
            new DateOnly(2024, 1, 4)
        );

        prices.Should().ContainSingle();
        var price = prices[0];
        price.Date.Should().Be(new DateOnly(2024, 1, 4));
        price.Open.Should().Be(185.1234m);
        price.High.Should().Be(187.6543m);
        price.Low.Should().Be(184.0009m);
        price.Close.Should().Be(186.7891m); // rounded from 186.789067 to 4 dp
        price.AdjustedClose.Should().Be(186.7891m); // no adjclose array -> falls back to close
        price.Volume.Should().Be(12345678L);
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
