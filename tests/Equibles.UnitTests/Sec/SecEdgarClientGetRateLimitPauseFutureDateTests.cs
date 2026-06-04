using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientGetRateLimitPauseFutureDateTests
{
    [Fact]
    public void GetRateLimitPause_RetryAfterFutureDateBeyondCeiling_CapsAtConfiguredPause()
    {
        // Contract: an absolute Retry-After Date in the future yields wait = date - now,
        // bounded by the ceiling (the configured 600s pause here). Existing pause tests
        // only cover a Delta or a PAST Date (-> zero); the future-Date arm that computes
        // `date - now` and caps it is unexercised. A date one hour out is far beyond the
        // 600s ceiling, so the result is deterministically the ceiling — no clock-race.
        // Oracle: 1h-future Date -> 600s, derived from the documented ceiling bound.
        var client = BuildClient();
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(
            DateTimeOffset.UtcNow.AddHours(1)
        );

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
