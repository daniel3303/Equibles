using System.Net;
using System.Text;
using Equibles.CommonStocks.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Pins Q4IncFeedClient.Fetch's contract: it returns null for a non-XML
/// response so callers can skip the feed. IR sites commonly serve an HTML
/// error page with HTTP 200, so a status-code-only check would hand that HTML
/// to the RSS parser as if it were feed data.
/// </summary>
public class Q4IncFeedClientFetchTests
{
    [Fact]
    public async Task Fetch_HtmlBodyWithOkStatus_ReturnsNull()
    {
        var handler = new StubHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<html><body>Page not found</body></html>",
                    Encoding.UTF8,
                    "text/html"
                ),
            }
        );
        var client = new Q4IncFeedClient(
            new HttpClient(handler),
            NullLogger<Q4IncFeedClient>.Instance
        );

        var result = await client.Fetch(
            "https://ir.example.com/",
            Q4IncFeedClient.NewsFeedPath,
            CancellationToken.None
        );

        result.Should().BeNull("an HTML body is not a feed, regardless of the 200 status");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(_response);
    }
}
