using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Sibling to YahooFinanceClientHistoricalTests and the ragged/holiday/local-date
/// pins. `GetHistoricalPrices` deliberately over-fetches by 1 day before and 2
/// after the requested window (to capture exchange-local boundary bars), then
/// trims with `if (date &lt; startDate || date &gt; endDate) continue`. The
/// trim is the only thing keeping over-fetched bars from leaking into the
/// caller's range — a refactor that compared against the over-fetched window
/// (period1/period2 bounds) by accident would silently grow every query's
/// result by up to 3 trading days, doubling the chart bar count and pulling in
/// data from outside the requested period. Pin the trim.
/// </summary>
public class YahooFinanceClientHistoricalOverFetchedTrimTests
{
    [Fact]
    public async Task GetHistoricalPrices_OverFetchedBarsOutsideRequestedRange_AreTrimmed()
    {
        // Yahoo returns FOUR bars at UTC midnight: 2024-12-22 (before window),
        // 2024-12-23, 2024-12-24 (in window), 2024-12-25 (after window).
        var unixDec22 = new DateTimeOffset(
            2024,
            12,
            22,
            0,
            0,
            0,
            TimeSpan.Zero
        ).ToUnixTimeSeconds();
        var unixDec23 = new DateTimeOffset(
            2024,
            12,
            23,
            0,
            0,
            0,
            TimeSpan.Zero
        ).ToUnixTimeSeconds();
        var unixDec24 = new DateTimeOffset(
            2024,
            12,
            24,
            0,
            0,
            0,
            TimeSpan.Zero
        ).ToUnixTimeSeconds();
        var unixDec25 = new DateTimeOffset(
            2024,
            12,
            25,
            0,
            0,
            0,
            TimeSpan.Zero
        ).ToUnixTimeSeconds();
        var json =
            "{\"chart\":{\"result\":[{\"timestamp\":["
            + unixDec22
            + ","
            + unixDec23
            + ","
            + unixDec24
            + ","
            + unixDec25
            + "],\"indicators\":{\"quote\":[{"
            + "\"open\":[99.00,100.10,100.50,101.00],"
            + "\"high\":[99.50,101.50,101.00,101.50],"
            + "\"low\":[98.80,99.80,100.00,100.50],"
            + "\"close\":[99.20,101.00,100.75,101.25],"
            + "\"volume\":[1000000,1500000,1200000,1100000]}]}}]}}";

        var handler = new StubHandler(json);
        var sut = new YahooFinanceClient(
            new HttpClient(handler),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var prices = await sut.GetHistoricalPrices(
            "AAPL",
            new DateOnly(2024, 12, 23),
            new DateOnly(2024, 12, 24)
        );

        prices.Should().HaveCount(2);
        prices
            .Select(p => p.Date)
            .Should()
            .Equal(new DateOnly(2024, 12, 23), new DateOnly(2024, 12, 24));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                }
            );
    }
}
