using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Sibling to <see cref="YahooFinanceClientKeyStatisticsTests"/> (pins
/// SharesOutstanding from defaultKeyStatistics) and the missing-shares /
/// empty-result variants. The market-cap feature added a second module
/// (summaryDetail) to the quote-summary request and a new field mapping:
/// <c>summaryDetail.marketCap.raw → KeyStatistics.MarketCapitalization</c>.
/// A regression that read MarketCap from the wrong module, forgot to include
/// <c>summaryDetail</c> in the URL (422), or read from the wrong JSON path
/// would silently produce zero MarketCap and SharesOutstanding-only pins
/// wouldn't notice.
/// </summary>
public class YahooFinanceClientKeyStatisticsMarketCapTests
{
    [Fact]
    public async Task GetKeyStatistics_SummaryDetailMarketCap_ParsesRawValueAndRequestsBothModules()
    {
        var json =
            "{\"quoteSummary\":{\"result\":[{"
            + "\"defaultKeyStatistics\":{\"sharesOutstanding\":{\"raw\":15500000000}},"
            + "\"summaryDetail\":{\"marketCap\":{\"raw\":2750000000000}}"
            + "}]}}";

        var handler = new CapturingHandler(json);
        var sut = new YahooFinanceClient(
            new HttpClient(handler),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var stats = await sut.GetKeyStatistics("AAPL");

        stats.Should().NotBeNull();
        stats!.MarketCapitalization.Should().Be(2_750_000_000_000d);
        // The single round-trip is the whole point of the feature comment —
        // both modules must appear in the URL or Yahoo returns 422.
        handler.LastUrl.Should().Contain("defaultKeyStatistics");
        handler.LastUrl.Should().Contain("summaryDetail");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _body;
        public string LastUrl { get; private set; }

        public CapturingHandler(string body) => _body = body;

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
