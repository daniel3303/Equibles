using System.Net;
using Equibles.Integrations.Fred;
using Equibles.Integrations.Fred.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Fred;

/// <summary>
/// `GetReleaseDates` feeds the economic release calendar. Pin three contracts:
/// the URL must request `include_release_dates_with_no_data=true` (that flag is
/// what surfaces FUTURE scheduled dates — dropping it silently empties the
/// upcoming calendar), the optional `realtime_start` must be appended in
/// yyyy-MM-dd, and the offset pagination must accumulate across pages (the
/// endpoint caps pages at 1000 rows).
/// </summary>
public class FredClientGetReleaseDatesTests
{
    [Fact]
    public async Task GetReleaseDates_RequestsFutureDates_AndPaginatesUntilCountReached()
    {
        var page1 = """
            {"count":3,"offset":0,"limit":1000,"release_dates":[
                {"release_id":10,"release_name":"Consumer Price Index","date":"2026-06-11"},
                {"release_id":50,"release_name":"Employment Situation","date":"2026-06-12"}
            ]}
            """;
        var page2 = """
            {"count":3,"offset":2,"limit":1000,"release_dates":[
                {"release_id":53,"release_name":"Gross Domestic Product","date":"2026-06-25"}
            ]}
            """;
        var handler = new ScriptedHandler(page1, page2);
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new FredOptions { ApiKey = "test-key" });
        var sut = new FredClient(httpClient, Substitute.For<ILogger<FredClient>>(), options);

        var result = await sut.GetReleaseDates();

        result.Should().HaveCount(3);
        result.Select(d => d.ReleaseId).Should().Equal(10, 50, 53);
        result.Select(d => d.Date).Should().Equal("2026-06-11", "2026-06-12", "2026-06-25");
        result[0].ReleaseName.Should().Be("Consumer Price Index");

        handler.Requests.Should().HaveCount(2);
        var firstQuery = handler.Requests[0].RequestUri!.Query;
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/fred/releases/dates");
        firstQuery.Should().Contain("include_release_dates_with_no_data=true");
        firstQuery.Should().NotContain("realtime_start");
        handler.Requests[1].RequestUri!.Query.Should().Contain("offset=2");
    }

    [Fact]
    public async Task GetReleaseDates_RealtimeStartSupplied_AppendsRealtimeStartParameter()
    {
        var empty = """{"count":0,"offset":0,"limit":1000,"release_dates":[]}""";
        var handler = new ScriptedHandler(empty);
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new FredOptions { ApiKey = "test-key" });
        var sut = new FredClient(httpClient, Substitute.For<ILogger<FredClient>>(), options);

        var result = await sut.GetReleaseDates(new DateOnly(2026, 1, 1));

        result.Should().BeEmpty();
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.Query.Should().Contain("realtime_start=2026-01-01");
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
