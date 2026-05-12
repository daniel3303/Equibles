using System.Net;
using Equibles.Integrations.Finra;
using Equibles.Integrations.Finra.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.Tests.Finra;

public class FinraClientTests {
    [Fact]
    public async Task GetDailyShortVolume_FirstPageBelowMaxPageSize_StopsAfterOneDataRequest() {
        // FinraClient paginates short-volume queries via offset. The loop must break
        // immediately once a page comes back smaller than MaxPageSize — without that
        // check, every import wastes an extra round-trip (and on partial pages where
        // the FINRA backend errors on out-of-bounds offsets, the import would fail).
        var tokenResponse = "{\"access_token\":\"test-token\",\"expires_in\":3600}";
        var dataResponse = "[{\"tradeReportDate\":\"2024-12-31\",\"securitiesInformationProcessorSymbolIdentifier\":\"AAPL\",\"totalParQuantity\":1000}]";

        var handler = new RoutingHandler(tokenResponse, dataResponse);
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new FinraOptions { ClientId = "id", ClientSecret = "secret" });
        var sut = new FinraClient(httpClient, Substitute.For<ILogger<FinraClient>>(), options);

        var result = await sut.GetDailyShortVolume(new DateOnly(2024, 12, 31));

        result.Should().HaveCount(1);
        result[0].Symbol.Should().Be("AAPL");
        handler.DataRequestCount.Should().Be(1);
    }

    private sealed class RoutingHandler : HttpMessageHandler {
        private readonly string _tokenBody;
        private readonly string _dataBody;
        public int DataRequestCount { get; private set; }

        public RoutingHandler(string tokenBody, string dataBody) {
            _tokenBody = tokenBody;
            _dataBody = dataBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var body = request.RequestUri!.AbsoluteUri.Contains("oauth2/access_token")
                ? _tokenBody
                : DataResponse();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(body),
            });
        }

        private string DataResponse() {
            DataRequestCount++;
            return _dataBody;
        }
    }
}
