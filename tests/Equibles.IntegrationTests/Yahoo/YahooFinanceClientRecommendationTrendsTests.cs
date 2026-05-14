using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Completes the trio of <see cref="YahooFinanceClient"/> public-method tests
/// (historical, key-statistics, recommendations). Pins <c>GetRecommendationTrends</c>
/// against a quoteSummary payload with two analyst periods. Both the <c>trend</c>
/// nested under <c>recommendationTrend.trend</c> and the <c>modules=recommendationTrend</c>
/// query string are load-bearing — a regression that aliased to <c>defaultKeyStatistics</c>
/// or flattened the model would return zero rows for every issuer with no exception.
/// </summary>
public class YahooFinanceClientRecommendationTrendsTests
{
    [Fact]
    public async Task GetRecommendationTrends_TwoPeriods_ParsesPeriodAndRatingCounts()
    {
        var json =
            "{\"quoteSummary\":{\"result\":[{\"recommendationTrend\":{\"trend\":["
            + "{\"period\":\"0m\",\"strongBuy\":12,\"buy\":18,\"hold\":7,\"sell\":1,\"strongSell\":0},"
            + "{\"period\":\"-1m\",\"strongBuy\":11,\"buy\":17,\"hold\":8,\"sell\":2,\"strongSell\":0}"
            + "]}}]}}";

        var handler = new CapturingHandler(json);
        var sut = new YahooFinanceClient(
            new HttpClient(handler),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var trends = await sut.GetRecommendationTrends("AAPL");

        trends.Should().HaveCount(2);
        trends[0].Period.Should().Be("0m");
        trends[0].StrongBuy.Should().Be(12);
        trends[0].Buy.Should().Be(18);
        trends[0].Hold.Should().Be(7);
        trends[1].Period.Should().Be("-1m");
        trends[1].StrongSell.Should().Be(0);

        handler.LastUrl.Should().Contain("modules=recommendationTrend");
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
