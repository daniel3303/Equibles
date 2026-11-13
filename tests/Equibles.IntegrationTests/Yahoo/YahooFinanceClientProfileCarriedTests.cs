using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

public class YahooFinanceClientProfileCarriedTests
{
    private const string Body =
        "{\"quoteSummary\":{\"result\":[{"
        + "\"defaultKeyStatistics\":{\"sharesOutstanding\":{\"raw\":1000}},"
        + "\"assetProfile\":{\"sector\":\"Technology\",\"industry\":\"Software\"}"
        + "}]}}";

    [Fact]
    public async Task EnrichmentReadsStatisticsAndProfileInOneRequest()
    {
        var handler = new CountingHandler(Body);
        var sut = new OfflineYahooFinanceClient(
            new HttpClient(handler),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var statistics = await sut.GetKeyStatistics("SAP.DE");
        var profile = await sut.GetCompanyProfile("SAP.DE");

        statistics.SharesOutstanding.Should().Be(1000);
        profile.Industry.Should().Be("Software");
        handler.Requests.Should().Be(1);
    }

    [Fact]
    public async Task ACarriedProfileIsServedOnceAndOnlyForItsOwnSymbol()
    {
        var handler = new CountingHandler(Body);
        var sut = new OfflineYahooFinanceClient(
            new HttpClient(handler),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        await sut.GetKeyStatistics("SAP.DE");
        await sut.GetCompanyProfile("SIE.DE");
        await sut.GetCompanyProfile("SAP.DE");
        await sut.GetCompanyProfile("SAP.DE");

        // Statistics, the other symbol's profile, then SAP's second profile read.
        handler.Requests.Should().Be(3);
    }

    private sealed class CountingHandler(string body) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
