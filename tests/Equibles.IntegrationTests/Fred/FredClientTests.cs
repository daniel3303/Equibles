using System.Net;
using Equibles.Integrations.Fred;
using Equibles.Integrations.Fred.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Fred;

public class FredClientTests
{
    [Fact]
    public async Task GetObservations_TotalExceedsFirstPage_AccumulatesAcrossPagesUntilCountReached()
    {
        // FRED returns up to 100k observations per call. When a series has more, the client
        // paginates via offset. Pinning the multi-page accumulation prevents a regression
        // that would silently truncate series at the first page.
        var page1 = """
            {"count":3,"offset":0,"observations":[
                {"date":"2024-01-01","value":"10.0"},
                {"date":"2024-01-02","value":"11.0"}
            ]}
            """;
        var page2 = """
            {"count":3,"offset":2,"observations":[
                {"date":"2024-01-03","value":"12.0"}
            ]}
            """;

        var handler = new ScriptedHandler(page1, page2);
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new FredOptions { ApiKey = "test-key" });
        var sut = new FredClient(httpClient, Substitute.For<ILogger<FredClient>>(), options);

        var result = await sut.GetObservations("DGS10");

        result.Should().HaveCount(3);
        result.Select(o => o.Date).Should().Equal("2024-01-01", "2024-01-02", "2024-01-03");
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].RequestUri!.Query.Should().Contain("offset=2");
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;
        public List<HttpRequestMessage> Requests { get; } = new();

        public ScriptedHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    "ScriptedHandler exhausted — pagination loop made more calls than expected."
                );
            }
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responses.Dequeue()),
                }
            );
        }
    }
}
