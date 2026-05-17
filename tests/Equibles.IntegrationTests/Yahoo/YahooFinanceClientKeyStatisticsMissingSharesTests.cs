using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Adversarial sibling to <see cref="YahooFinanceClientKeyStatisticsEmptyResultTests"/>
/// (no stats module → null) and <c>YahooFinanceClientKeyStatisticsTests</c>
/// (fully populated). Neither covers the branch where the
/// <c>defaultKeyStatistics</c> module IS present but carries no
/// <c>sharesOutstanding</c> — a real Yahoo quirk for ETFs / index proxies.
/// Contract: the module being present means GetKeyStatistics must return a
/// KeyStatistics (not null — null is reserved for "no module at all"), with
/// SharesOutstanding gracefully defaulting to 0, never throwing.
/// </summary>
public class YahooFinanceClientKeyStatisticsMissingSharesTests
{
    [Fact]
    public async Task GetKeyStatistics_StatsModulePresentButSharesOutstandingMissing_ReturnsZeroNotNull()
    {
        // defaultKeyStatistics exists (so the stats==null guard is NOT hit) but
        // has no sharesOutstanding field at all.
        var json = "{\"quoteSummary\":{\"result\":[{\"defaultKeyStatistics\":{}}]}}";

        var sut = new YahooFinanceClient(
            new HttpClient(new ConstantHandler(json)),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var stats = await sut.GetKeyStatistics("ETFNOSHARES");

        stats.Should().NotBeNull("a present defaultKeyStatistics module must yield a result");
        stats!.SharesOutstanding.Should().Be(0);
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
