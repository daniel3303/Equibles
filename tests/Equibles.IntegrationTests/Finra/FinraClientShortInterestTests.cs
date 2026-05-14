using System.Net;
using System.Text;
using Equibles.Integrations.Finra;
using Equibles.Integrations.Finra.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// Sibling to <see cref="FinraClientTests"/>. That file pins the short-volume
/// pagination break. This pins the <see cref="FinraClient.GetShortInterest(DateOnly, IReadOnlyList{string})"/>
/// symbol-filtered overload — the <c>symbols != null</c> branch in
/// <c>GetShortInterestCore</c> is otherwise unreachable, and it carries the
/// <c>domainFilters</c> payload that FINRA's API rejects when malformed.
/// </summary>
public class FinraClientShortInterestTests
{
    [Fact]
    public async Task GetShortInterest_WithSymbolFilter_PostsDomainFilterAndParsesRecords()
    {
        var tokenResponse = "{\"access_token\":\"test-token\",\"expires_in\":3600}";
        var dataResponse =
            "[{\"settlementDate\":\"2024-12-15\",\"symbolCode\":\"AAPL\","
            + "\"issueName\":\"Apple Inc.\",\"currentShortPositionQuantity\":12345,"
            + "\"previousShortPositionQuantity\":10000,\"changePreviousNumber\":2345,"
            + "\"averageDailyVolumeQuantity\":1000000,\"daysToCoverQuantity\":1.23,"
            + "\"changePercent\":23.45,\"marketClassCode\":\"NMS\"}]";

        var handler = new CapturingHandler(tokenResponse, dataResponse);
        var sut = new FinraClient(
            new HttpClient(handler),
            Substitute.For<ILogger<FinraClient>>(),
            Options.Create(new FinraOptions { ClientId = "id", ClientSecret = "secret" })
        );

        var result = await sut.GetShortInterest(new DateOnly(2024, 12, 15), ["AAPL"]);

        result.Should().ContainSingle();
        var record = result[0];
        record.Symbol.Should().Be("AAPL");
        record.IssueName.Should().Be("Apple Inc.");
        record.CurrentShortPosition.Should().Be(12345);
        record.DaysToCover.Should().Be(1.23m);

        // The symbol-filter branch must serialise a domainFilters[] block — its absence
        // would silently widen the query to every issuer and dump megabytes back.
        handler.LastDataRequestBody.Should().Contain("\"domainFilters\"");
        handler.LastDataRequestBody.Should().Contain("\"AAPL\"");
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
