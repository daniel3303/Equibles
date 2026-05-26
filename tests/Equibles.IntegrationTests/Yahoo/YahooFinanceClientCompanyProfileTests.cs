using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Sibling to YahooFinanceClientKeyStatisticsTests (which pins
/// GetKeyStatistics). GetCompanyProfile is the third public quoteSummary
/// method — it shares the same GetQuoteSummaryResult plumbing but requests
/// `modules=assetProfile` and projects Sector / Industry / Website /
/// LongBusinessSummary out of the nested `assetProfile` container. The whole
/// method body is uncovered; a refactor that flattened the JSON model or
/// renamed any of the four `JsonProperty` keys would silently null out the
/// company-profile sidebar in production with no failing test.
/// </summary>
public class YahooFinanceClientCompanyProfileTests
{
    [Fact]
    public async Task GetCompanyProfile_ValidPayload_ParsesSectorIndustryWebsiteAndSummary()
    {
        var json =
            "{\"quoteSummary\":{\"result\":[{\"assetProfile\":{"
            + "\"sector\":\"Technology\","
            + "\"industry\":\"Consumer Electronics\","
            + "\"website\":\"https://www.apple.com\","
            + "\"longBusinessSummary\":\"Designs, manufactures, and markets smartphones.\""
            + "}}]}}";

        var handler = new CapturingHandler(json);
        var sut = new YahooFinanceClient(
            new HttpClient(handler),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var profile = await sut.GetCompanyProfile("AAPL");

        profile.Should().NotBeNull();
        profile.Sector.Should().Be("Technology");
        profile.Industry.Should().Be("Consumer Electronics");
        profile.Website.Should().Be("https://www.apple.com");
        profile.LongBusinessSummary.Should().Be("Designs, manufactures, and markets smartphones.");

        // modules=assetProfile distinguishes the request from key-statistics /
        // recommendation-trend calls. Yahoo returns 422 without it.
        handler.LastUrl.Should().Contain("modules=assetProfile");
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
