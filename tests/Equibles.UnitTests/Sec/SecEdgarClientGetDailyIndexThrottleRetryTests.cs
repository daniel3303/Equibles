using System.Net;
using System.Net.Http.Headers;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins GetDailyIndex's throttle-retry path: a 403 "Request Rate Threshold
/// Exceeded" page on a PAST date (whose index always exists) is retried and the
/// recovered master index parsed — not swallowed as "no index". A regression
/// returning empty on that 403 silently drops the day's filings during a sweep,
/// which is how large filers went missing (GH-2222).
/// </summary>
public class SecEdgarClientGetDailyIndexThrottleRetryTests
{
    [Fact]
    public async Task GetDailyIndex_ThrottlePageThenSuccess_RetriesAndParses()
    {
        var threshold =
            "<html><head><title>SEC.gov | Request Rate Threshold Exceeded</title></head></html>";
        var master =
            "CIK|Company Name|Form Type|Date Filed|File Name\n"
            + "933478|VANGUARD FIDUCIARY TRUST CO|13F-HR|20200102|edgar/data/933478/0000933478-20-000004.txt\n";
        var handler = new ThrottleThenOkHandler(threshold, master);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var sut = new SecEdgarClient(
            new HttpClient(handler),
            NullLogger<SecEdgarClient>.Instance,
            config
        );

        // A past date: its daily index always exists, so the 403 must be treated
        // as a throttle to retry, never as "no index for this day".
        var result = await sut.GetDailyIndex(new DateOnly(2020, 1, 2));

        // Two calls prove the 403 drove a retry that then succeeded and parsed.
        handler.CallCount.Should().Be(2);
        result.Should().ContainSingle();
        result[0].AccessionNumber.Should().Be("0000933478-20-000004");
    }

    private sealed class ThrottleThenOkHandler : HttpMessageHandler
    {
        private readonly string _throttleBody;
        private readonly string _okBody;
        public int CallCount { get; private set; }

        public ThrottleThenOkHandler(string throttleBody, string okBody)
        {
            _throttleBody = throttleBody;
            _okBody = okBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            if (CallCount == 1)
            {
                var throttled = new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(_throttleBody),
                };
                // Retry-After: 0 keeps the test fast and avoids pausing the shared rate limiter.
                throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(throttled);
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_okBody) }
            );
        }
    }
}
