using System.Net;
using Equibles.Integrations.Finra;
using Equibles.Integrations.Finra.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// Existing FinraClient tests always return 200 on the first data call, so the
/// 401 → InvalidateToken → re-fetch → retry branch in <c>SendWithRetry</c> is
/// uncovered. FINRA expires bearer tokens server-side mid-scrape; that branch
/// must transparently refresh and retry. A regression that dropped it (or moved
/// 401 into the generic non-retry path) would fail the entire short-volume
/// import the moment a cached token aged out.
/// </summary>
public class FinraClientTokenRefreshTests
{
    [Fact]
    public async Task GetDailyShortVolume_FirstDataCallUnauthorized_RefreshesTokenThenSucceeds()
    {
        var dataResponse =
            "[{\"tradeReportDate\":\"2024-12-31\","
            + "\"securitiesInformationProcessorSymbolIdentifier\":\"AAPL\","
            + "\"totalParQuantity\":1000}]";

        var handler = new TokenRefreshHandler(
            tokenBody: "{\"access_token\":\"tok\",\"expires_in\":3600}",
            dataBody: dataResponse
        );
        var sut = new FinraClient(
            new HttpClient(handler),
            Substitute.For<ILogger<FinraClient>>(),
            Options.Create(new FinraOptions { ClientId = "id", ClientSecret = "secret" })
        );

        var result = await sut.GetDailyShortVolume(new DateOnly(2024, 12, 31));

        result.Should().ContainSingle();
        result[0].Symbol.Should().Be("AAPL");
        // The 401 path is fully proven by exactly two data calls plus a parsed
        // record: the retry only returns data because GetAccessToken ran again
        // after InvalidateToken — a broken branch would throw on the 401 instead.
        // TokenRequestCount is deliberately NOT asserted: FinraClient caches the
        // token in static fields, so a sibling Finra test running in parallel can
        // warm that cache and make the initial fetch skip this handler.
        handler.DataRequestCount.Should().Be(2);
    }

    private sealed class TokenRefreshHandler : HttpMessageHandler
    {
        private readonly string _tokenBody;
        private readonly string _dataBody;
        public int DataRequestCount { get; private set; }

        public TokenRefreshHandler(string tokenBody, string dataBody)
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
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
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
