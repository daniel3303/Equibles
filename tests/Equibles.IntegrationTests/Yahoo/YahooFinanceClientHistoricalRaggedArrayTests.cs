using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

public class YahooFinanceClientHistoricalRaggedArrayTests
{
    // Contract: GetHistoricalPrices returns the parseable daily prices from a
    // Yahoo chart payload. The code already defends against upstream payload
    // irregularities (null Close on holidays, missing adjclose). A ragged
    // payload — timestamp array longer than the OHLC arrays, a real Yahoo
    // quirk — must likewise be tolerated, not crash the whole ticker import.
    // (adjclose IS bounds-checked `i < adjCloseList.Count`; OHLC is not.)
    [Fact]
    public async Task GetHistoricalPrices_TimestampArrayLongerThanQuoteArrays_DoesNotThrow()
    {
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

        // Two timestamps, but every OHLC/volume array has only ONE element.
        var json =
            "{\"chart\":{\"result\":[{\"timestamp\":["
            + unixDec23
            + ","
            + unixDec24
            + "],\"indicators\":{\"quote\":[{"
            + "\"open\":[100.10],\"high\":[101.50],\"low\":[99.80],"
            + "\"close\":[101.00],\"volume\":[1500000]}]}}]}}";

        var sut = new YahooFinanceClient(
            new HttpClient(new StubHandler(json)),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var prices = await sut.GetHistoricalPrices(
            "AAPL",
            new DateOnly(2024, 12, 23),
            new DateOnly(2024, 12, 24)
        );

        prices.Should().ContainSingle();
        prices[0].Date.Should().Be(new DateOnly(2024, 12, 23));
        prices[0].Close.Should().Be(101.00m);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
