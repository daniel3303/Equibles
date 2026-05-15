using System.Net;
using Equibles.Integrations.Finra;
using Equibles.Integrations.Finra.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// Sibling to <see cref="FinraClientTokenRefreshTests"/>, which pins the 401
/// branch of SendWithRetry. This pins the 5xx branch (lines 362-373, zero-hit):
/// FINRA's data API intermittently 500s under load; that response must trigger
/// a transparent retry, not abort the whole short-volume import. A regression
/// moving 5xx into the EnsureSuccessStatusCode path would fail every nightly
/// scrape the moment FINRA hiccups once.
/// </summary>
public class FinraClientServerErrorRetryTests
{
    [Fact]
    public async Task GetDailyShortVolume_FirstDataCallServerError_RetriesThenSucceeds()
    {
        var dataResponse =
            "[{\"tradeReportDate\":\"2024-12-31\","
            + "\"securitiesInformationProcessorSymbolIdentifier\":\"AAPL\","
            + "\"totalParQuantity\":1000}]";

        var handler = new ServerErrorThenOkHandler(
            tokenBody: "{\"access_token\":\"tok\",\"expires_in\":3600}",
            dataBody: dataResponse
        );
        var sut = new FinraClient(
            new HttpClient(handler),
            Substitute.For<ILogger<FinraClient>>(),
            Options.Create(new FinraOptions { ClientId = "id", ClientSecret = "secret" })
        );

        var result = await sut.GetDailyShortVolume(new DateOnly(2024, 12, 31));

        // Two data calls + a parsed record prove the 500 drove a retry that then
        // succeeded — a broken 5xx branch would throw on the first response.
        result.Should().ContainSingle();
        result[0].Symbol.Should().Be("AAPL");
        handler.DataRequestCount.Should().Be(2);
    }

    private sealed class ServerErrorThenOkHandler : HttpMessageHandler
    {
        private readonly string _tokenBody;
        private readonly string _dataBody;
        public int DataRequestCount { get; private set; }

        public ServerErrorThenOkHandler(string tokenBody, string dataBody)
        {
            _tokenBody = tokenBody;
            _dataBody = dataBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.RequestUri!.AbsoluteUri.Contains("oauth2/access_token"))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(_tokenBody),
                    }
                );
            }

            DataRequestCount++;
            if (DataRequestCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_dataBody),
                }
            );
        }
    }
}
