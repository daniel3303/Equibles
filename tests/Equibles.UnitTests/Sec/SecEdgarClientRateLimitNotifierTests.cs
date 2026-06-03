using System.Net;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins that SecEdgarClient signals its rate-limit notifier: a throttled
/// response calls RateLimited and a successful one calls Reachable. The edge
/// de-duplication lives in the notifier (see
/// <see cref="SecRateLimitEventPublisherTests"/>); the client only forwards, so
/// these assertions are count-based and order-free. Sec:RateLimitPauseSeconds=0
/// keeps the retry pause instant.
/// </summary>
public class SecEdgarClientRateLimitNotifierTests
{
    private const string DocumentBody = "<SEC-DOCUMENT>ok</SEC-DOCUMENT>";

    private static SecEdgarClient BuildClient(
        HttpMessageHandler handler,
        ISecRateLimitNotifier notifier
    )
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["Sec:ContactEmail"] = "test@example.com",
                    ["Sec:RateLimitPauseSeconds"] = "0",
                }
            )
            .Build();
        return new SecEdgarClient(
            new HttpClient(handler),
            NullLogger<SecEdgarClient>.Instance,
            config,
            notifier
        );
    }

    [Fact]
    public async Task Send_429ThenSuccess_SignalsRateLimitedThenReachable()
    {
        var notifier = new RecordingNotifier();
        var handler = new SequencedHandler(
            () => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            () => Ok(DocumentBody)
        );
        var sut = BuildClient(handler, notifier);

        var content = await sut.GetDocumentContent("0000899243-22-038492", "1932393");

        content.Should().Be(DocumentBody);
        notifier.RateLimitedCount.Should().BeGreaterThan(0);
        notifier.ReachableCount.Should().Be(1);
    }

    [Fact]
    public async Task Send_Success_SignalsReachableNotRateLimited()
    {
        var notifier = new RecordingNotifier();
        var handler = new SequencedHandler(() => Ok(DocumentBody));
        var sut = BuildClient(handler, notifier);

        await sut.GetDocumentContent("0000899243-22-038492", "1932393");

        notifier.ReachableCount.Should().Be(1);
        notifier.RateLimitedCount.Should().Be(0);
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class RecordingNotifier : ISecRateLimitNotifier
    {
        public int RateLimitedCount { get; private set; }
        public int ReachableCount { get; private set; }

        public Task RateLimited(TimeSpan pause, string url)
        {
            RateLimitedCount++;
            return Task.CompletedTask;
        }

        public Task Reachable(string url)
        {
            ReachableCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage>[] _responses;
        public int CallCount { get; private set; }

        public SequencedHandler(params Func<HttpResponseMessage>[] responses) =>
            _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var factory = _responses[Math.Min(CallCount, _responses.Length - 1)];
            CallCount++;
            return Task.FromResult(factory());
        }
    }
}
