using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Sibling to <see cref="YahooFinanceClientHistoricalTests"/>, which pins the
/// populated chart path. This pins the empty path: Yahoo returns
/// <c>{"chart":{"result":[]}}</c> for a delisted or invalid ticker, so
/// <c>result?.Chart?.Result?.FirstOrDefault()</c> is null. <c>GetHistoricalPrices</c>
/// must return an empty list via the <c>result?.Timestamp == null</c> guard, not
/// NRE walking <c>result.Timestamp</c>. The daily price scrape iterates every
/// tracked stock; dropping that guard would crash the whole run on the first
/// ticker Yahoo no longer charts.
/// </summary>
public class YahooFinanceClientHistoricalEmptyResultTests
{
    [Fact]
    public async Task GetHistoricalPrices_EmptyChartResult_ReturnsEmptyList()
    {
        var json = "{\"chart\":{\"result\":[]}}";

        var sut = new YahooFinanceClient(
            new HttpClient(new StubHandler(json)),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var prices = await sut.GetHistoricalPrices(
            "DELISTED",
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31)
        );

        prices.Should().BeEmpty();
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
