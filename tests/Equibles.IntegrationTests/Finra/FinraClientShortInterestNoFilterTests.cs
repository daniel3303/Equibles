using System.Net;
using System.Text;
using Equibles.Integrations.Finra;
using Equibles.Integrations.Finra.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// Sibling to <see cref="FinraClientShortInterestTests"/>, which pins the
/// symbol-filtered overload. This pins the unfiltered
/// <see cref="FinraClient.GetShortInterest(DateOnly)"/> overload — the
/// <c>symbols == null</c> <c>else</c> branch in <c>GetShortInterestCore</c>,
/// otherwise unreachable. That branch must build the query *without* a
/// <c>domainFilters</c> block; a regression that always emitted
/// <c>domainFilters</c> (e.g. collapsing the two query shapes into one) would
/// post an empty symbol-domain filter and silently return zero rows for the
/// full-market short-interest scrape.
/// </summary>
public class FinraClientShortInterestNoFilterTests
{
    [Fact]
    public async Task GetShortInterest_NoSymbolFilter_OmitsDomainFiltersAndParsesRecords()
    {
        var tokenResponse = "{\"access_token\":\"test-token\",\"expires_in\":3600}";
        var dataResponse =
            "[{\"settlementDate\":\"2024-12-15\",\"symbolCode\":\"MSFT\","
            + "\"issueName\":\"Microsoft Corp.\",\"currentShortPositionQuantity\":54321,"
            + "\"previousShortPositionQuantity\":50000,\"changePreviousNumber\":4321,"
            + "\"averageDailyVolumeQuantity\":2000000,\"daysToCoverQuantity\":2.5,"
            + "\"changePercent\":8.64,\"marketClassCode\":\"NMS\"}]";

        var handler = new CapturingHandler(tokenResponse, dataResponse);
        var sut = new FinraClient(
            new HttpClient(handler),
            Substitute.For<ILogger<FinraClient>>(),
            Options.Create(new FinraOptions { ClientId = "id", ClientSecret = "secret" })
        );

        var result = await sut.GetShortInterest(new DateOnly(2024, 12, 15));

        result.Should().ContainSingle();
        result[0].Symbol.Should().Be("MSFT");
        result[0].CurrentShortPosition.Should().Be(54321);

        // The unfiltered branch must NOT serialise a domainFilters[] block while
        // still scoping by settlementDate — emitting an (empty) domain filter
        // would zero out the full-market result.
        handler.LastDataRequestBody.Should().NotContain("domainFilters");
        handler.LastDataRequestBody.Should().Contain("dateRangeFilters");
        handler.LastDataRequestBody.Should().Contain("2024-12-15");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _tokenBody;
        private readonly string _dataBody;
        public string LastDataRequestBody { get; private set; }

        public CapturingHandler(string tokenBody, string dataBody)
        {
            _tokenBody = tokenBody;
            _dataBody = dataBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var isToken = request.RequestUri!.AbsoluteUri.Contains("oauth2/access_token");
            string body;
            if (isToken)
            {
                body = _tokenBody;
            }
            else
            {
                LastDataRequestBody =
                    request.Content != null
                        ? await request.Content.ReadAsStringAsync(cancellationToken)
                        : "";
                body = _dataBody;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
