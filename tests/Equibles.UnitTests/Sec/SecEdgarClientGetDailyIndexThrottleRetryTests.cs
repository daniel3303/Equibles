using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins GetDailyIndex's 403 handling. SEC has no index on non-trading days
/// (weekends and market holidays such as Memorial Day) and answers those from its
/// S3-backed Archives with a 403 carrying an AccessDenied/NoSuchKey body — for
/// PAST dates included. That must be skipped so the sweep advances. A 403 carrying
/// the rolling-window throttle page must NEVER be skipped (that would silently drop
/// a trading day's filings) — it is retried, then surfaced if it persists. Any
/// other, unrecognized 403 is treated conservatively as a fetch failure, not "no
/// filings".
/// </summary>
public class SecEdgarClientGetDailyIndexThrottleRetryTests
{
    private const string MasterIndexBody =
        "CIK|Company Name|Form Type|Date Filed|File Name\n"
        + "933478|VANGUARD FIDUCIARY TRUST CO|13F-HR|20200102|edgar/data/933478/0000933478-20-000004.txt\n";

    private const string ThrottlePageBody =
        "<html><head><title>Request Rate Threshold Exceeded</title></head></html>";

    // The S3 error body SEC returns for a date that has no index object.
    private const string AccessDeniedBody =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
        + "<Error><Code>AccessDenied</Code><Message>Access Denied</Message></Error>";

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
    public async Task GetDailyIndex_ThrottlePageThenSuccess_RetriesAndParses()
    {
        // The rolling-window throttle page is retryable; the next attempt succeeds.
        var handler = new SequencedHandler(
            () => Forbidden(ThrottlePageBody),
            () => Ok(MasterIndexBody)
        );
        var sut = BuildClient(handler);

        var result = await sut.GetDailyIndex(new DateOnly(2020, 1, 2));

        handler.CallCount.Should().Be(2);
        result.Should().ContainSingle();
        result[0].AccessionNumber.Should().Be("0000933478-20-000004");
    }

    [Fact]
    public async Task GetDailyIndex_ThrottlePagePersists_ThrowsAfterRetriesNeverSkips()
    {
        // Every attempt is throttled. A persistent throttle is a real fetch failure,
        // not "no filings": it must be retried and then surfaced (the caller catches
        // it per day and holds the sweep watermark back), never silently skipped.
        var handler = new SequencedHandler(() => Forbidden(ThrottlePageBody));
        var sut = BuildClient(handler);

        var act = async () => await sut.GetDailyIndex(new DateOnly(2020, 1, 2));

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.CallCount.Should().BeGreaterThan(1); // retried before giving up
    }

    [Fact]
    public async Task GetDailyIndex_PastDateMissingIndex_ReturnsEmptyWithoutRetry()
    {
        // Memorial Day 2026-05-25 (a PAST date when swept) has no index — SEC answers
        // with an AccessDenied 403. It must be skipped, not retried as throttling.
        var handler = new SequencedHandler(() => Forbidden(AccessDeniedBody));
        var sut = BuildClient(handler);

        var result = await sut.GetDailyIndex(new DateOnly(2026, 5, 25));

        result.Should().BeEmpty();
        handler.CallCount.Should().Be(1); // not retried
    }

    [Fact]
    public async Task GetDailyIndex_GzippedMissingIndex_DecodesBodyAndReturnsEmpty()
    {
        // SEC gzip-compresses its S3 error bodies even without an Accept-Encoding
        // request, so the missing-index 403 must be decoded before it can be told
        // apart from a throttle. The raw bytes would never match either signature.
        var handler = new SequencedHandler(() => GzippedForbidden(AccessDeniedBody));
        var sut = BuildClient(handler);

        var result = await sut.GetDailyIndex(new DateOnly(2026, 5, 25));

        result.Should().BeEmpty();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyIndex_GzippedThrottlePagePersists_RetriesAndThrows()
    {
        // A throttle page must be recognized even when gzip-compressed, so it is
        // backed off and retried — never skipped, and never thrown on the first try.
        var handler = new SequencedHandler(() => GzippedForbidden(ThrottlePageBody));
        var sut = BuildClient(handler);

        var act = async () => await sut.GetDailyIndex(new DateOnly(2020, 1, 2));

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.CallCount.Should().BeGreaterThan(1); // retried, not skipped
    }

    [Fact]
    public async Task GetDailyIndex_NoSuchKeyMissingIndex_ReturnsEmpty()
    {
        // S3 also returns NoSuchKey (not only AccessDenied) for a missing index object.
        const string noSuchKey =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<Error><Code>NoSuchKey</Code><Message>The specified key does not exist.</Message></Error>";
        var handler = new SequencedHandler(() => Forbidden(noSuchKey));
        var sut = BuildClient(handler);

        var result = await sut.GetDailyIndex(new DateOnly(2026, 5, 25));

        result.Should().BeEmpty();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyIndex_NotFound_ReturnsEmpty()
    {
        // Weekends typically 404 — unambiguously "no index for this date".
        var handler = new SequencedHandler(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = BuildClient(handler);

        var result = await sut.GetDailyIndex(new DateOnly(2020, 1, 4));

        result.Should().BeEmpty();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyIndex_UnrecognizedForbidden_ThrowsRatherThanSkip()
    {
        // A 403 that is neither the throttle page nor a recognizable missing-index
        // error is ambiguous: treat it as a fetch failure rather than risk skipping a
        // day that has filings.
        var handler = new SequencedHandler(() => Forbidden("something unexpected"));
        var sut = BuildClient(handler);

        var act = async () => await sut.GetDailyIndex(new DateOnly(2020, 1, 2));

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.CallCount.Should().Be(1); // not the throttle page → not retried
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

    private static HttpResponseMessage GzippedForbidden(string body)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            gzip.Write(bytes, 0, bytes.Length);
        }

        var content = new ByteArrayContent(output.ToArray());
        content.Headers.ContentEncoding.Add("gzip");

        var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = content };
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
