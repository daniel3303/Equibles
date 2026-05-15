using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Sibling to <see cref="YahooFinanceClientKeyStatisticsTests"/>, which pins the
/// populated path. This pins the null path: when Yahoo returns a well-formed
/// envelope with an empty <c>quoteSummary.result</c> array (the response for a
/// ticker Yahoo has no key-statistics module for), <c>GetKeyStatistics</c> must
/// return <c>null</c> — not throw an NRE off <c>FirstOrDefault()</c>. The price
/// import service treats <c>null</c> as "skip this issuer"; a regression that
/// dropped the <c>stats == null</c> guard would crash the whole scrape on the
/// first issuer with no stats.
/// </summary>
public class YahooFinanceClientKeyStatisticsEmptyResultTests
{
    [Fact]
    public async Task GetKeyStatistics_EmptyQuoteSummaryResult_ReturnsNull()
    {
        var json = "{\"quoteSummary\":{\"result\":[]}}";

        var sut = new YahooFinanceClient(
            new HttpClient(new ConstantHandler(json)),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var stats = await sut.GetKeyStatistics("NOSTATS");

        stats.Should().BeNull();
    }

    private sealed class ConstantHandler : HttpMessageHandler
    {
        private readonly string _body;

        public ConstantHandler(string body) => _body = body;

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
