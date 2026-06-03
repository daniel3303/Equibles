using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins SendWithRetryAsync's handling of SEC's rate-limit response. SEC answers an
/// over-limit IP with a 429 (or its 403 "Request Rate Threshold Exceeded" page) and
/// sends NO Retry-After. That 429 must be treated as a throttle — the whole rate
/// limiter is paused for the configured penalty window and the request retried — so
/// the IP block can auto-lift, rather than surfaced or hammered with a short backoff
/// that only renews the block. Sec:RateLimitPauseSeconds is set to 0 here so the pause
/// is instant and the suite stays fast; the production default is 10 minutes.
/// </summary>
public class SecEdgarClientRateLimitPauseTests
{
    private const string DocumentBody = "<SEC-DOCUMENT>full submission text</SEC-DOCUMENT>";

    private static SecEdgarClient BuildClient(HttpMessageHandler handler)
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
            config
        );
    }

    [Fact]
    public async Task GetDocumentContent_429NoRetryAfterThenSuccess_RetriesAndReturnsContent()
    {
        // SEC's 429 carries no Retry-After; it is a throttle, not a hard failure, so
        // the next attempt after the pause must succeed and return the document.
        var handler = new SequencedHandler(
            () => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            () => Ok(DocumentBody)
        );
        var sut = BuildClient(handler);

        var content = await sut.GetDocumentContent("0000899243-22-038492", "1932393");

        content.Should().Be(DocumentBody);
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task GetDocumentContent_429Persists_RetriesThenThrows()
    {
        // A persistent 429 is a real failure: it must be retried (never given up on
        // the first response) and then surfaced once retries are exhausted.
        var handler = new SequencedHandler(() =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        );
        var sut = BuildClient(handler);

        var act = async () => await sut.GetDocumentContent("0000899243-22-038492", "1932393");

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.CallCount.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task GetDocumentContent_429WithRetryAfterThenSuccess_HonorsHeaderAndRetries()
    {
        // When SEC does send a Retry-After it is honored (here 0, to stay fast)
        // instead of the default penalty window, then the retry succeeds.
        var handler = new SequencedHandler(
            () => TooManyRequests(TimeSpan.Zero),
            () => Ok(DocumentBody)
        );
        var sut = BuildClient(handler);

        var content = await sut.GetDocumentContent("0000899243-22-038492", "1932393");

        content.Should().Be(DocumentBody);
        handler.CallCount.Should().Be(2);
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage TooManyRequests(TimeSpan retryAfter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            retryAfter
        );
        return response;
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
            // Repeat the last configured response once the sequence is exhausted.
            var factory = _responses[Math.Min(CallCount, _responses.Length - 1)];
            CallCount++;
            return Task.FromResult(factory());
        }
    }
}
