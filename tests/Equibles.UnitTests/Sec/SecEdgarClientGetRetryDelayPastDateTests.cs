using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial Lane A. The `Retry-After` header's HTTP-date form names an
/// absolute instant (e.g. "Wed, 21 Oct 2015 07:28:00 GMT"). When SEC's
/// edge has wall-clock skew, or when retry pipelines surface an already-
/// expired header from a queued response, the named instant lies in the
/// past — `date - DateTimeOffset.UtcNow` yields a negative `TimeSpan`. The
/// guard `if (wait &gt; TimeSpan.Zero)` must drop that and fall through to
/// `TransientBackoff(attempt)`; otherwise the negative span propagates into
/// `Task.Delay(TimeSpan)`, which throws `ArgumentOutOfRangeException` and
/// kills the entire retry loop. A refactor that "trusts the header value
/// either way" or collapses both arms into a single `return wait`-style
/// expression would silently break SendWithRetryAsync for any clock-skewed
/// upstream — symptomless until the next live throttle, then catastrophic.
/// </summary>
public class SecEdgarClientGetRetryDelayPastDateTests
{
    [Fact]
    public void GetRetryDelay_RetryAfterDateInThePast_FallsThroughToTransientBackoff()
    {
        using var httpClient = new HttpClient();
        var sut = new SecEdgarClient(
            httpClient,
            NullLogger<SecEdgarClient>.Instance,
            new ConfigurationBuilder().Build()
        );

        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        // Date one hour ago — clock skew / queued-response shape. Setting the
        // RetryAfter via DateTimeOffset populates the `.Date` arm.
        response.Headers.RetryAfter = new RetryConditionHeaderValue(
            DateTimeOffset.UtcNow.AddHours(-1)
        );

        var method = typeof(SecEdgarClient).GetMethod(
            "GetRetryDelay",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var delay = (TimeSpan)method.Invoke(sut, [response, 0]);

        // TransientBackoff(0) = 2^(0+1) s = 2s. The exact value pins the
        // fallback shape; the critical invariant is non-negative.
        delay
            .Should()
            .BeGreaterThan(
                TimeSpan.Zero,
                "a negative delay would crash Task.Delay and kill the retry loop"
            );
        delay
            .Should()
            .Be(
                TimeSpan.FromSeconds(2),
                "past-date Retry-After must fall through to TransientBackoff(attempt)"
            );
    }
}
