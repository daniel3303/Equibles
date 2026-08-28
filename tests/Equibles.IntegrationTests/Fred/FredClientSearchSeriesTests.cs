using Equibles.Integrations.Fred;
using Equibles.Integrations.Fred.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Fred;

public class FredClientSearchSeriesTests
{
    [Fact]
    public async Task SearchSeries_EncodesQueryBoundsLimitAndReturnsMetadata()
    {
        var handler = new CapturingHandler(
            """
            {"seriess":[{"id":"DEXTAUS","title":"Taiwan Dollars to U.S. Dollar Exchange Rate","frequency":"Daily","units":"New Taiwan Dollars to One U.S. Dollar","notes":"Noon buying rates."}]}
            """
        );
        var client = new FredClient(
            new HttpClient(handler),
            Substitute.For<ILogger<FredClient>>(),
            Options.Create(new FredOptions { ApiKey = "test-key" })
        );

        var result = await client.SearchSeries("TWD per U.S. dollar", 500);

        Assert.Single(result);
        Assert.Equal("DEXTAUS", result[0].Id);
        Assert.Contains("search_text=TWD%20per%20U.S.%20dollar", handler.RequestUri.Query);
        Assert.Contains("limit=100", handler.RequestUri.Query);
        Assert.Contains("api_key=test-key", handler.RequestUri.Query);
    }

    [Fact]
    public async Task SearchSeries_BlankQuery_DoesNotCallApi()
    {
        var handler = new CapturingHandler("{}");
        var client = new FredClient(
            new HttpClient(handler),
            Substitute.For<ILogger<FredClient>>(),
            Options.Create(new FredOptions { ApiKey = "test-key" })
        );

        var result = await client.SearchSeries("  ");

        Assert.Empty(result);
        Assert.Null(handler.RequestUri);
    }

    private sealed class CapturingHandler(string response) : HttpMessageHandler
    {
        public Uri RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(response),
                }
            );
        }
    }
}
