using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Sibling to <see cref="YahooFinanceClientRecommendationTrendsTests"/>, which
/// pins the populated two-period path. This pins the empty path: Yahoo returns
/// <c>{"quoteSummary":{"result":[]}}</c> for a ticker with no analyst coverage,
/// so the trend list resolves to null. <c>GetRecommendationTrends</c> must
/// return an empty list via the <c>trends == null</c> guard, not NRE walking a
/// null <c>Trend</c>. The recommendation scrape iterates every tracked stock;
/// dropping that guard would crash the whole run on the first uncovered issuer.
/// </summary>
public class YahooFinanceClientRecommendationTrendsEmptyTests
{
    [Fact]
    public async Task GetRecommendationTrends_EmptyQuoteSummaryResult_ReturnsEmptyList()
    {
        var json = "{\"quoteSummary\":{\"result\":[]}}";

        var sut = new YahooFinanceClient(
            new HttpClient(new StubHandler(json)),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var trends = await sut.GetRecommendationTrends("NOCOVERAGE");

        trends.Should().BeEmpty();
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
