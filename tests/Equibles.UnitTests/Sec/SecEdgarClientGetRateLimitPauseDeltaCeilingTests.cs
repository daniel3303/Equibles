using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientGetRateLimitPauseDeltaCeilingTests
{
    [Fact]
    public void GetRateLimitPause_RetryAfterDeltaExceedingConfiguredPause_CapsAtConfiguredPauseNotMaxRetryDelay()
    {
        // Contract: GetRateLimitPause bounds a too-large Retry-After so a hostile/stale
        // header can't idle a processor forever — but its ceiling is the CONFIGURED
        // rate-limit pause (here 600s), which is larger than the 5-minute transient
        // MaxRetryDelay used by GetRetryDelay. Existing pause tests only send a Delta of
        // 0 or a past Date, so the over-ceiling Delta cap is unexercised. The plausible
        // regression: "unifying" the cap to MaxRetryDelay would clamp to 300s, under-
        // pausing and renewing SEC's block. Oracle: 30min Delta -> 600s, not 300s.
        var client = BuildClient();
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(30));

        var method = typeof(SecEdgarClient).GetMethod(
            "GetRateLimitPause",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var pause = (TimeSpan)method.Invoke(client, [response]);

        pause.Should().Be(TimeSpan.FromSeconds(600));
    }

    private static SecEdgarClient BuildClient()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["Sec:ContactEmail"] = "test@example.com",
                    ["Sec:RateLimitPauseSeconds"] = "600",
                }
            )
            .Build();
        return new SecEdgarClient(new HttpClient(), NullLogger<SecEdgarClient>.Instance, config);
    }
}
