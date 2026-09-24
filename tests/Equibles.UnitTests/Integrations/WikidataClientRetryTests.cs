using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Wikidata;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Integrations;

/// <summary>
/// Contract: a throttled (429) or overloaded (5xx) Wikidata answer is retried a bounded number
/// of times, waiting the service's own Retry-After when it sends one, and a persistent outage
/// surfaces as <see cref="WikidataUnavailableException"/> rather than a generic HTTP failure.
/// A 429 also pauses the shared limiter; a client error is not retried.
/// </summary>
public class WikidataClientRetryTests
{
    private const string EmptyResults = """{"results":{"bindings":[]}}""";

    private static (
        WikidataClient Client,
        ScriptedHandler Handler,
        List<TimeSpan> Waits,
        IRateLimiter Limiter
    ) BuildSut(params Func<HttpResponseMessage>[] responses)
    {
        var handler = new ScriptedHandler(responses);
        var waits = new List<TimeSpan>();
        var limiter = Substitute.For<IRateLimiter>();
        var client = new WikidataClient(
            new HttpClient(handler),
            Substitute.For<ILogger<WikidataClient>>()
        )
        {
            Limiter = limiter,
            Delay = (wait, _) =>
            {
                waits.Add(wait);
                return Task.CompletedTask;
            },
        };
        return (client, handler, waits, limiter);
    }

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                EmptyResults,
                Encoding.UTF8,
                "application/sparql-results+json"
            ),
        };

    private static HttpResponseMessage Status(HttpStatusCode status, TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(status);
        if (retryAfter is { } delta)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(delta);
        return response;
    }

    [Fact]
    public async Task Throttle_WaitsTheServiceRetryAfter_ThenSucceeds()
    {
        var (client, handler, waits, limiter) = BuildSut(
            () => Status(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(7)),
            Ok
        );

        var result = await client.GetOfficialWebsitesByCik(["320193"], CancellationToken.None);

        result.Should().BeEmpty();
        handler.Calls.Should().Be(2);
        waits.Should().Equal(TimeSpan.FromSeconds(7));
        limiter.Received(1).PauseFor(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task BadGateway_IsRetriedWithBackoff_WithoutPausingTheLimiter()
    {
        var (client, handler, waits, limiter) = BuildSut(
            () => Status(HttpStatusCode.BadGateway),
            Ok
        );

        await client.GetOfficialWebsitesByCik(["320193"], CancellationToken.None);

        handler.Calls.Should().Be(2);
        waits.Should().ContainSingle().Which.Should().BePositive();
        limiter.DidNotReceive().PauseFor(Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task PersistentOutage_SurfacesAsUnavailable_AfterBoundedAttempts()
    {
        var (client, handler, waits, _) = BuildSut(
            () => Status(HttpStatusCode.BadGateway),
            () => Status(HttpStatusCode.ServiceUnavailable),
            () => Status(HttpStatusCode.BadGateway),
            Ok
        );

        var act = () =>
            client.GetOfficialWebsitesByLei(["5493000IBP32UQZ0KL24"], CancellationToken.None);

        (await act.Should().ThrowAsync<WikidataUnavailableException>())
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.BadGateway);
        handler.Calls.Should().Be(3, "the fourth scripted answer must never be requested");
        waits.Should().HaveCount(2);
    }

    [Fact]
    public async Task RetryAfterBeyondTheCap_IsNotWaitedOut_InsideTheBatch()
    {
        var (client, handler, waits, limiter) = BuildSut(
            () => Status(HttpStatusCode.TooManyRequests, TimeSpan.FromMinutes(10)),
            Ok
        );

        var act = () => client.GetOfficialWebsitesByCik(["320193"], CancellationToken.None);

        await act.Should().ThrowAsync<WikidataUnavailableException>();
        handler.Calls.Should().Be(1);
        waits.Should().BeEmpty();
        limiter.Received(1).PauseFor(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task ClientError_IsNotRetried_AndIsNotReportedAsUnavailable()
    {
        var (client, handler, waits, _) = BuildSut(() => Status(HttpStatusCode.BadRequest), Ok);

        var act = () => client.GetOfficialWebsitesByCik(["320193"], CancellationToken.None);

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Should()
            .NotBeOfType<WikidataUnavailableException>();
        handler.Calls.Should().Be(1);
        waits.Should().BeEmpty();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses;

        public ScriptedHandler(IEnumerable<Func<HttpResponseMessage>> responses) =>
            _responses = new(responses);

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return Task.FromResult(_responses.Dequeue()());
        }
    }
}
