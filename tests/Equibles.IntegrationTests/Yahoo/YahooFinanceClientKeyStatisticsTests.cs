using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Sibling to <see cref="YahooFinanceClientHistoricalTests"/>. That pins
/// <c>GetHistoricalPrices</c>; this pins <c>GetKeyStatistics</c> — the other public
/// HTTP method, otherwise uncovered. The Yahoo quoteSummary payload nests the
/// shares-outstanding value under <c>defaultKeyStatistics.sharesOutstanding.raw</c>;
/// a regression that flattened the model away from the <c>{ raw: N }</c> wrapper
/// (or dropped the <c>modules=defaultKeyStatistics</c> query parameter, which would
/// 422 the request) would silently return zero shares for every issuer.
/// </summary>
public class YahooFinanceClientKeyStatisticsTests
{
    [Fact]
    public async Task GetKeyStatistics_ValidPayload_ParsesSharesOutstandingRawValue()
    {
        var json =
            "{\"quoteSummary\":{\"result\":[{\"defaultKeyStatistics\":{"
            + "\"sharesOutstanding\":{\"raw\":15500000000},"
            + "\"enterpriseValue\":{\"raw\":3000000000000}}}]}}";

        var handler = new CapturingHandler(json);
        var sut = new YahooFinanceClient(
            new HttpClient(handler),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var stats = await sut.GetKeyStatistics("AAPL");

        stats.Should().NotBeNull();
        stats.SharesOutstanding.Should().Be(15_500_000_000);

        // modules=defaultKeyStatistics is the only thing distinguishing this request
        // from the recommendationTrend call. Yahoo returns a 422 without it.
        handler.LastUrl.Should().Contain("modules=defaultKeyStatistics");
        handler.LastUrl.Should().Contain("AAPL");
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
