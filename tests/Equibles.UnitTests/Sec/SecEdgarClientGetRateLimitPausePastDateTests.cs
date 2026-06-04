using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins GetRateLimitPause for an absolute Retry-After Date already in the past. The
/// existing pause tests only ever send a relative Delta, so the Date arm is unexercised.
/// A Date whose instant has elapsed means SEC's block window is already over, so the
/// pause must be zero — not the full configured penalty — otherwise a stale timestamp
/// would idle a processor for the whole ceiling. The configured pause is 600s here, so
/// a fall-through to the default would surface as a non-zero result.
/// </summary>
public class SecEdgarClientGetRateLimitPausePastDateTests
{
    [Fact]
    public void GetRateLimitPause_RetryAfterDateInPast_ReturnsZero()
    {
        var client = BuildClient();
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(
            DateTimeOffset.UtcNow.AddMinutes(-5)
        );

        var method = typeof(SecEdgarClient).GetMethod(
            "GetRateLimitPause",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var pause = (TimeSpan)method.Invoke(client, [response]);

        pause.Should().Be(TimeSpan.Zero);
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
