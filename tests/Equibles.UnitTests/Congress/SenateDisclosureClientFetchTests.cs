using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Pins SenateDisclosureClient's fetch/retry/pagination orchestration, now
/// testable after extracting the Playwright transport behind
/// <see cref="ISenateBrowserSession"/>. Covers the search→report flow,
/// multi-page pagination, the 429 and SenateBrowserException retry branches,
/// the non-2xx hard-fail, and disposal delegation. The HTML transaction parser
/// has its own dedicated tests; these assert the orchestration only.
/// </summary>
public class SenateDisclosureClientFetchTests
{
    private const string ReportUrl = "https://efdsearch.senate.gov/search/view/ptr/abc123/";

    private static string SearchJson(int recordsTotal, int rowCount)
    {
        var rows = string.Join(
            ",",
            Enumerable
                .Range(0, rowCount)
                .Select(_ =>
                    "[\"John\",\"Doe\",\"x\","
                    + "\"<a href=\\\"/search/view/ptr/abc123/\\\">View</a>\","
                    + "\"2024-01-15\"]"
                )
        );
        return $"{{\"draw\":1,\"recordsTotal\":{recordsTotal},"
            + $"\"recordsFiltered\":{recordsTotal},\"data\":[{rows}],\"result\":\"ok\"}}";
    }

    private static SenateFetchResult Ok(string body) => new() { Status = 200, Body = body };

    private sealed class FakeSenateBrowserSession : ISenateBrowserSession
    {
        public Queue<Func<SenateFetchResult>> Script { get; } = new();
        public List<string> FetchedUrls { get; } = [];
        public bool Disposed { get; private set; }

        public Task EnsureAuthenticated(CancellationToken ct) => Task.CompletedTask;

        public Task<SenateFetchResult> Fetch(
            string url,
            Dictionary<string, string> formFields,
            CancellationToken ct
        )
        {
            FetchedUrls.Add(url);
            return Task.FromResult(Script.Dequeue()());
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private static SenateDisclosureClient Sut(FakeSenateBrowserSession session) =>
        new(session, Substitute.For<ILogger<SenateDisclosureClient>>());

    [Fact]
    public async Task GetRecentTransactions_SearchThenReport_FetchesEachReportUrl()
    {
        var session = new FakeSenateBrowserSession();
        session.Script.Enqueue(() => Ok(SearchJson(recordsTotal: 1, rowCount: 1)));
        session.Script.Enqueue(() => Ok("<html><body>no transactions</body></html>"));

        var result = await Sut(session)
            .GetRecentTransactions(
                new DateOnly(2024, 1, 1),
                new DateOnly(2024, 1, 31),
                CancellationToken.None
            );

        result.Should().NotBeNull();
        session.FetchedUrls.Should().Contain(ReportUrl);
    }

    private static string SearchJsonInvalidRows(int recordsTotal, int rowCount)
    {
        // Rows with no href → ParseReportRow rejects them, so pagination is
        // exercised without triggering a report fetch per row.
        var rows = string.Join(
            ",",
            Enumerable
                .Range(0, rowCount)
                .Select(_ => "[\"John\",\"Doe\",\"x\",\"no-link-here\",\"2024-01-15\"]")
        );
        return $"{{\"draw\":1,\"recordsTotal\":{recordsTotal},"
            + $"\"recordsFiltered\":{recordsTotal},\"data\":[{rows}],\"result\":\"ok\"}}";
    }

    [Fact]
    public async Task GetRecentTransactions_RecordsTotalSpansTwoPages_PaginatesUntilExhausted()
    {
        const string searchUrl = "https://efdsearch.senate.gov/search/report/data/";
        var session = new FakeSenateBrowserSession();
        // recordsTotal 150 with pageSize 100 → exactly two search pages.
        session.Script.Enqueue(() => Ok(SearchJsonInvalidRows(recordsTotal: 150, rowCount: 100)));
        session.Script.Enqueue(() => Ok(SearchJsonInvalidRows(recordsTotal: 150, rowCount: 50)));

        await Sut(session)
            .GetRecentTransactions(
                new DateOnly(2024, 1, 1),
                new DateOnly(2024, 1, 31),
                CancellationToken.None
            );

        // Two search pages fetched, zero reports (all rows rejected).
        session.FetchedUrls.Should().AllBe(searchUrl);
        session.FetchedUrls.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRecentTransactions_FirstSearchReturns429ThenSucceeds_RetriesTransparently()
    {
        var session = new FakeSenateBrowserSession();
        session.Script.Enqueue(() => new SenateFetchResult { Status = 429, Body = "" });
        session.Script.Enqueue(() => Ok(SearchJson(1, 1)));
        session.Script.Enqueue(() => Ok("<html></html>"));

        var result = await Sut(session)
            .GetRecentTransactions(
                new DateOnly(2024, 1, 1),
                new DateOnly(2024, 1, 31),
                CancellationToken.None
            );

        result.Should().NotBeNull();
        session.FetchedUrls.Should().Contain(ReportUrl);
    }

    [Fact]
    public async Task GetRecentTransactions_FirstSearchThrowsBrowserExceptionThenSucceeds_Retries()
    {
        var session = new FakeSenateBrowserSession();
        var thrown = false;
        session.Script.Enqueue(() =>
        {
            thrown = true;
            throw new SenateBrowserException("boom", new InvalidOperationException());
        });
        session.Script.Enqueue(() => Ok(SearchJson(1, 1)));
        session.Script.Enqueue(() => Ok("<html></html>"));

        var result = await Sut(session)
            .GetRecentTransactions(
                new DateOnly(2024, 1, 1),
                new DateOnly(2024, 1, 31),
                CancellationToken.None
            );

        thrown.Should().BeTrue();
        result.Should().NotBeNull();
        session.FetchedUrls.Should().Contain(ReportUrl);
    }

    [Fact]
    public async Task GetRecentTransactions_SearchReturnsNonRetryable404_ThrowsHttpRequestException()
    {
        var session = new FakeSenateBrowserSession();
        session.Script.Enqueue(() => new SenateFetchResult { Status = 404, Body = "missing" });

        var act = () =>
            Sut(session)
                .GetRecentTransactions(
                    new DateOnly(2024, 1, 1),
                    new DateOnly(2024, 1, 31),
                    CancellationToken.None
                );

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task DisposeAsync_DelegatesToSession()
    {
        var session = new FakeSenateBrowserSession();

        await Sut(session).DisposeAsync();

        session.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task GetRecentTransactions_SearchThrowsBrowserExceptionEveryAttempt_ExhaustsRetriesAndRethrows()
    {
        // The "throws once then succeeds" pin covers the retry+continue arm;
        // this covers the exhaustion arm: a SenateBrowserException on every
        // attempt (0..MaxRetries) must, on the final attempt, log and rethrow
        // rather than continue or swallow.
        var session = new FakeSenateBrowserSession();
        for (var attempt = 0; attempt <= 3; attempt++)
        {
            session.Script.Enqueue(() =>
                throw new SenateBrowserException("down", new InvalidOperationException())
            );
        }

        var act = async () =>
            await Sut(session)
                .GetRecentTransactions(
                    new DateOnly(2024, 1, 1),
                    new DateOnly(2024, 1, 31),
                    CancellationToken.None
                );

        await act.Should().ThrowAsync<SenateBrowserException>();
        session.FetchedUrls.Should().HaveCount(4, "every attempt (0..MaxRetries) was made");
    }

    [Fact]
    public async Task GetRecentTransactions_ReportFetchHardFails_LogsAndContinuesWithoutThrowing()
    {
        // Search yields one report, but fetching that report 404s (non-retryable
        // → HttpRequestException). The per-report catch must log and move on so
        // one bad report can't abort the whole run.
        var session = new FakeSenateBrowserSession();
        session.Script.Enqueue(() => Ok(SearchJson(recordsTotal: 1, rowCount: 1)));
        session.Script.Enqueue(() => new SenateFetchResult { Status = 404, Body = "gone" });

        var result = await Sut(session)
            .GetRecentTransactions(
                new DateOnly(2024, 1, 1),
                new DateOnly(2024, 1, 31),
                CancellationToken.None
            );

        result.Should().BeEmpty("the only report failed to fetch, but the run completed");
        session.FetchedUrls.Should().Contain(ReportUrl);
    }

    [Fact]
    public async Task GetRecentTransactions_ReportFetchCancelled_RethrowsOperationCanceled()
    {
        // Search succeeds; the report fetch raises OperationCanceledException.
        // The per-report handler must rethrow it (cancellation is not a
        // per-report failure to swallow).
        var session = new FakeSenateBrowserSession();
        session.Script.Enqueue(() => Ok(SearchJson(recordsTotal: 1, rowCount: 1)));
        session.Script.Enqueue(() => throw new OperationCanceledException());

        var act = async () =>
            await Sut(session)
                .GetRecentTransactions(
                    new DateOnly(2024, 1, 1),
                    new DateOnly(2024, 1, 31),
                    CancellationToken.None
                );

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
