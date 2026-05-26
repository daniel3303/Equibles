using System.Net;
using System.Net.Http.Headers;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins GetDailyIndex's 403 handling (GH-2222). A daily index for a PAST date
/// always exists, so a 403 there is SEC throttling — it must be retried
/// regardless of the response body, and if throttling persists the day is
/// skipped (logged) rather than crashing the whole sweep. For today/future
/// dates a 403 means "not yet published" and must not be retried.
/// </summary>
public class SecEdgarClientGetDailyIndexThrottleRetryTests
{
    private const string MasterIndexBody =
        "CIK|Company Name|Form Type|Date Filed|File Name\n"
        + "933478|VANGUARD FIDUCIARY TRUST CO|13F-HR|20200102|edgar/data/933478/0000933478-20-000004.txt\n";

    private static SecEdgarClient BuildClient(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        return new SecEdgarClient(
            new HttpClient(handler),
            NullLogger<SecEdgarClient>.Instance,
            config
        );
    }

    [Fact]
    public async Task GetDailyIndex_PastDateForbiddenThenSuccess_RetriesRegardlessOfBodyAndParses()
    {
        // Empty 403 body — proves the retry is driven by the past-date rule, not
        // by recognizing the throttle page (real throttle 403s can be body-less).
        var handler = new SequencedHandler(() => Forbidden(body: ""), () => Ok(MasterIndexBody));
        var sut = BuildClient(handler);

        var result = await sut.GetDailyIndex(new DateOnly(2020, 1, 2));

        handler.CallCount.Should().Be(2);
        result.Should().ContainSingle();
        result[0].AccessionNumber.Should().Be("0000933478-20-000004");
    }

    [Fact]
    public async Task GetDailyIndex_PastDateThrottlePersists_ReturnsEmptyWithoutThrowing()
    {
        // Every attempt is throttled. The day must be skipped (empty), never
        // surfaced as the unhandled 403 that aborted the whole realtime sweep.
        var handler = new SequencedHandler(() => Forbidden(body: ""));
        var sut = BuildClient(handler);

        var result = await sut.GetDailyIndex(new DateOnly(2020, 1, 2));

        result.Should().BeEmpty();
        handler.CallCount.Should().BeGreaterThan(1); // retried before giving up
    }

    [Fact]
    public async Task GetDailyIndex_FutureDateForbidden_ReturnsEmptyWithoutRetry()
    {
        // A future date's index isn't published yet: 403 means "no index", not
        // throttling, so it must not be retried.
        var handler = new SequencedHandler(() => Forbidden(body: ""));
        var sut = BuildClient(handler);

        var result = await sut.GetDailyIndex(new DateOnly(2099, 1, 1));

        result.Should().BeEmpty();
        handler.CallCount.Should().Be(1);
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage Forbidden(string body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(body),
        };
        // Retry-After: 0 keeps retrying tests fast and avoids a real backoff pause.
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
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
