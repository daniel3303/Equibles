using System.Net;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Common.Retry;
using NSubstitute;

namespace Equibles.UnitTests.Integrations;

public class HttpRetrySendNonRetryableStatusTests
{
    // Contract: Send retries ONLY on 429 or 5xx; any other non-success status (e.g. 404) must
    // surface immediately via EnsureSuccessStatusCode — so a generous maxRetries can't turn a
    // hard 404 into N pointless re-sends (and N×backoff of real delay). A regression that
    // broadened the retry guard to "any non-success" would re-send 4× and never pause the
    // limiter for the 404 either. Pin: send invoked exactly once, throws, no limiter pause.
    [Fact]
    public async Task Send_NonRetryableNotFound_DoesNotRetryAndThrows()
    {
        var limiter = Substitute.For<IRateLimiter>();
        var sendCount = 0;
        Func<Task<HttpResponseMessage>> send = () =>
        {
            sendCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };

        var act = () =>
            HttpRetry.Send(
                send,
                limiter,
                maxRetries: 3,
                "exhausted",
                (_, _) => { },
                (_, _, _) => { }
            );

        await act.Should().ThrowAsync<HttpRequestException>();
        sendCount.Should().Be(1);
        limiter.DidNotReceive().PauseFor(Arg.Any<TimeSpan>());
    }
}
