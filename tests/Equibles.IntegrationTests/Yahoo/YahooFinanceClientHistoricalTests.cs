using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Unit-tier <c>YahooFinanceClientTests</c> covers only argument validation and
/// browser-header / unix-timestamp helpers. Every HTTP-driven public method
/// (<c>GetHistoricalPrices</c>, <c>GetRecommendationTrends</c>, <c>GetKeyStatistics</c>)
/// is uncovered. This test pins <c>GetHistoricalPrices</c> against a real Yahoo-shaped
/// chart payload with one trading day and one market holiday (null Close) — proving
/// the inner loop skips the null and that the AdjustedClose fallback falls back to
/// the day's Close when the <c>adjclose</c> column is omitted.
/// </summary>
public class YahooFinanceClientHistoricalTests
{
    [Fact]
    public async Task GetHistoricalPrices_OneTradingDayOneHoliday_SkipsHolidayAndAppliesAdjCloseFallback()
    {
        // Two timestamps: 2024-12-23 (Monday, real prices) and 2024-12-25 (Christmas, all nulls).
        // No adjclose array supplied — pins the `adjCloseList == null` fallback to Close.
        var unixDec23 = new DateTimeOffset(
            2024,
            12,
            23,
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
            + unixDec23
            + ","
            + unixDec25
            + "],\"indicators\":{\"quote\":[{"
            + "\"open\":[100.10,null],\"high\":[101.50,null],\"low\":[99.80,null],"
            + "\"close\":[101.00,null],\"volume\":[1500000,null]}]}}]}}";

        var handler = new StubHandler(json);
        var sut = new YahooFinanceClient(
            new HttpClient(handler),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var prices = await sut.GetHistoricalPrices(
            "AAPL",
            new DateOnly(2024, 12, 23),
            new DateOnly(2024, 12, 25)
        );

        // The holiday must be skipped — a regression that drops the `quote.Close[i] == null`
        // guard would emit a NaN row or a row with all zeros.
        prices.Should().ContainSingle();
        var row = prices[0];
        row.Date.Should().Be(new DateOnly(2024, 12, 23));
        row.Open.Should().Be(100.10m);
        row.Close.Should().Be(101.00m);
        // AdjustedClose falls back to Close when the chart payload has no adjclose array.
        row.AdjustedClose.Should().Be(101.00m);
        row.Volume.Should().Be(1500000);

        // URL must include period1/period2 and interval=1d — a regression that drops the
        // interval or swaps period order would silently change the granularity to "5m"
        // (Yahoo's default), bloating responses and breaking the daily-close contract.
        handler.LastUrl.Should().Contain("interval=1d");
        handler.LastUrl.Should().Contain("period1=");
        handler.LastUrl.Should().Contain("period2=");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public string LastUrl { get; private set; }

        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastUrl = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
