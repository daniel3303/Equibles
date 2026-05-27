using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Sibling to YahooFinanceClientKeyStatisticsEmptyResultTests (empty result
/// array → null) and YahooFinanceClientKeyStatisticsTests (populated). The
/// third null-handling branch — a non-empty result object that nevertheless
/// has BOTH defaultKeyStatistics AND summaryDetail missing — is structurally
/// distinct: it's the case Yahoo returns when the ticker is recognised but
/// neither module is available (delisted ADRs, OTC tickers with no analyst
/// coverage). Without the explicit both-null guard, the method would emit a
/// KeyStatistics with SharesOutstanding=0 / MarketCapitalization=0 — the
/// price-import worker would store these zeros and corrupt every downstream
/// market-cap chart. Pin the null return.
/// </summary>
public class YahooFinanceClientKeyStatisticsBothModulesMissingTests
{
    [Fact]
    public async Task GetKeyStatistics_NonEmptyResultButBothModulesMissing_ReturnsNullInsteadOfZeros()
    {
        // Result object is present but carries no defaultKeyStatistics and no
        // summaryDetail (a real Yahoo response shape for low-coverage tickers).
        var json = "{\"quoteSummary\":{\"result\":[{}]}}";

        var sut = new YahooFinanceClient(
            new HttpClient(new ConstantHandler(json)),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var stats = await sut.GetKeyStatistics("LOWCOV");

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
