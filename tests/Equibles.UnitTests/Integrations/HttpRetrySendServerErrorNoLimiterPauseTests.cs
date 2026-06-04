using System.Net;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Common.Retry;
using NSubstitute;

namespace Equibles.UnitTests.Integrations;

public class HttpRetrySendServerErrorNoLimiterPauseTests
{
    // Contract (doc-comment): "Only a 429 pauses the shared limiter (a 5xx is not
    // a rate-limit signal)." A 503 must be retried WITHOUT calling PauseFor —
    // pausing the shared limiter on a server error would needlessly throttle every
    // other client sharing it. The existing pin covers the non-retryable 404; this
    // covers the 5xx-retries-but-does-not-pause path.
    [Fact]
    public async Task Send_ServerErrorThenSuccess_RetriesWithoutPausingLimiter()
    {
        var limiter = Substitute.For<IRateLimiter>();
        var sendCount = 0;
        Func<Task<HttpResponseMessage>> send = () =>
        {
            sendCount++;
            return Task.FromResult(
                new HttpResponseMessage(
                    sendCount == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK
                )
            );
        };

        var response = await HttpRetry.Send(
            send,
            limiter,
            maxRetries: 3,
            "exhausted",
            (_, _) => { },
            (_, _, _) => { }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sendCount.Should().Be(2);
        limiter.DidNotReceive().PauseFor(Arg.Any<TimeSpan>());
    }
}
