using System.Net;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Models;
using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Cboe;

public class CboeClientTests
{
    private const string DailyStatsBaseUrl =
        "https://www.cboe.com/markets/us/options/market-statistics/daily/";
    private const string VixUrl =
        "https://cdn.cboe.com/api/global/us_indices/daily_prices/VIX_History.csv";

    [Fact]
    public async Task DownloadDailyPutCallRatios_RequestsDailyPageWithDateQueryParam()
    {
        // The endpoint is the live CBOE daily-statistics HTML page; the date
        // parameter (`?dt=YYYY-MM-DD`) selects which trading day's data the
        // page renders. A typo in the URL or the date format would silently
        // pull "today" instead of the requested date and overwrite the wrong
        // row on every cycle. Pin the exact URL shape.
        var handler = new ScriptedHandler((HttpStatusCode.OK, ""));
        var sut = CreateSut(handler);

        await sut.DownloadDailyPutCallRatios(new DateOnly(2026, 4, 15));

        handler
            .Requests.Should()
            .ContainSingle()
            .Which.RequestUri!.ToString()
            .Should()
            .Be($"{DailyStatsBaseUrl}?dt=2026-04-15");
    }

    [Fact]
    public async Task DownloadDailyPutCallRatios_NonTradingDayPage_ReturnsEmpty()
    {
        // CBOE renders the page skeleton for weekends/holidays/future dates
        // but omits the optionsData block. The parser must return an empty
        // dictionary — not throw and not produce phantom rows.
        const string nonTradingHtml = "<html><body>No data for selected date.</body></html>";
        var handler = new ScriptedHandler((HttpStatusCode.OK, nonTradingHtml));
        var sut = CreateSut(handler);

        var result = await sut.DownloadDailyPutCallRatios(new DateOnly(2025, 12, 25));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadDailyPutCallRatios_TradingDayPage_ExtractsRatioAndVolumePerProduct()
    {
        // The page embeds optionsData as a JSON object inside the React Server
        // Component payload, with each `"` escaped to `\"`. The client must
        // (a) locate the optionsData marker, (b) walk balanced braces escape-
        // aware, (c) JSON-unescape the slice, (d) parse it, and (e) map the
        // ratios list + per-category VOLUME row to the 5 product types.
        //
        // A regression in any of those phases would drop or corrupt put/call
        // data across the entire dashboard. The page-shape is also the most
        // likely thing to drift over time (CBOE rewrites the front-end every
        // couple years) — pin the shape we depend on.
        var html = BuildDailyPageHtml(
            totalRatio: "0.82",
            equityRatio: "0.47",
            indexRatio: "1.11",
            vixRatio: "0.51",
            etpRatio: "1.01",
            totalCall: 7_165_995,
            totalPut: 5_891_495,
            totalVolume: 13_057_490,
            equityCall: 2_960_846,
            equityPut: 1_388_890,
            equityVolume: 4_349_736
        );
        var handler = new ScriptedHandler((HttpStatusCode.OK, html));
        var sut = CreateSut(handler);
        var date = new DateOnly(2026, 5, 26);

        var result = await sut.DownloadDailyPutCallRatios(date);

        result
            .Should()
            .ContainKey(CboePutCallProductType.Total)
            .WhoseValue.Should()
            .BeEquivalentTo(
                new CboePutCallRecord
                {
                    Date = date,
                    CallVolume = 7_165_995,
                    PutVolume = 5_891_495,
                    TotalVolume = 13_057_490,
                    PutCallRatio = 0.82m,
                }
            );
        result
            .Should()
            .ContainKey(CboePutCallProductType.Equity)
            .WhoseValue.Should()
            .BeEquivalentTo(
                new CboePutCallRecord
                {
                    Date = date,
                    CallVolume = 2_960_846,
                    PutVolume = 1_388_890,
                    TotalVolume = 4_349_736,
                    PutCallRatio = 0.47m,
                }
            );
        result[CboePutCallProductType.Index].PutCallRatio.Should().Be(1.11m);
        result[CboePutCallProductType.Vix].PutCallRatio.Should().Be(0.51m);
        result[CboePutCallProductType.Etp].PutCallRatio.Should().Be(1.01m);
    }

    [Fact]
    public async Task DownloadVixHistory_OkResponse_FetchesVixHistoryUrlAndReturnsParsedRecords()
    {
        // Pin the VIX endpoint URL and confirm the full DownloadVixHistory ->
        // ParseVixCsv flow on a well-formed feed. A typo or a future change
        // to a different CBOE CDN path would silently break the VIX ingest
        // in production. Asserting on the requested URL fixes the contract.
        var csv = "DATE,OPEN,HIGH,LOW,CLOSE\n" + "01/03/2020,13.72,14.49,13.51,14.02\n";
        var handler = new ScriptedHandler((HttpStatusCode.OK, csv));
        var sut = CreateSut(handler);

        var result = await sut.DownloadVixHistory();

        handler.Requests.Should().ContainSingle().Which.RequestUri!.ToString().Should().Be(VixUrl);
        result
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new CboeVixRecord
                {
                    Date = new DateOnly(2020, 1, 3),
                    Open = 13.72m,
                    High = 14.49m,
                    Low = 13.51m,
                    Close = 14.02m,
                }
            );
    }

    [Fact]
    public async Task DownloadWithRetry_TooManyRequestsThenOk_RetriesAndReturnsParsedContent()
    {
        // 429 path: the loop must back off (RateLimiter.PauseFor + Task.Delay)
        // and try again, returning the eventual success body. Without the
        // retry the first CBOE 429 of the day would abort the whole ingest.
        // The initial delay is 2^(0+1) = 2s, so this test pays a real 2s
        // wait — keep it to a single retry. The success on attempt #2 also
        // proves the loop EXITS the retry branch instead of looping forever.
        // The fake limiter records PauseFor so we can assert the 429 path
        // called it (vs the 5xx path).
        var handler = new ScriptedHandler(
            (HttpStatusCode.TooManyRequests, ""),
            (HttpStatusCode.OK, "")
        );
        var rateLimiter = Substitute.For<IRateLimiter>();
        var sut = CreateSut(handler, rateLimiter);

        await sut.DownloadDailyPutCallRatios(new DateOnly(2026, 4, 15));

        handler.Requests.Should().HaveCount(2);
        rateLimiter.Received(1).PauseFor(Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task DownloadWithRetry_ServerErrorThenOk_RetriesAndReturnsParsedContent()
    {
        // 5xx path is structurally distinct from the 429 path: it does NOT
        // call RateLimiter.PauseFor (server errors aren't a rate-limit
        // signal), only Task.Delay. Pin the retry-on-5xx -> success flow
        // so a refactor that collapses both branches into one or drops the
        // 5xx retry is caught.
        var csv = "DATE,OPEN,HIGH,LOW,CLOSE\n" + "01/03/2020,13.72,14.49,13.51,14.02\n";
        var handler = new ScriptedHandler(
            (HttpStatusCode.BadGateway, ""),
            (HttpStatusCode.OK, csv)
        );
        var rateLimiter = Substitute.For<IRateLimiter>();
        var sut = CreateSut(handler, rateLimiter);

        var result = await sut.DownloadVixHistory();

        handler.Requests.Should().HaveCount(2);
        result.Should().ContainSingle();
        rateLimiter.DidNotReceive().PauseFor(Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task DownloadWithRetry_NonRetryableStatus_ThrowsImmediately()
    {
        // 404 (and any other non-2xx, non-429, non-5xx) is not retryable —
        // EnsureSuccessStatusCode throws on the first attempt. Pin the
        // single-request behavior so a refactor that broadens the retry
        // window (e.g. "retry on any error") isn't shipped silently:
        // retrying a permanent 404 burns the rate-limit budget for nothing.
        var handler = new ScriptedHandler((HttpStatusCode.NotFound, ""));
        var sut = CreateSut(handler);

        var act = async () => await sut.DownloadDailyPutCallRatios(new DateOnly(2026, 4, 15));

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.Requests.Should().ContainSingle();
    }

    private static CboeClient CreateSut(ScriptedHandler handler, IRateLimiter rateLimiter = null) =>
        new(
            new HttpClient(handler),
            Substitute.For<ILogger<CboeClient>>(),
            rateLimiter ?? Substitute.For<IRateLimiter>()
        );

    // Builds a minimal HTML page in the shape the live CBOE daily-statistics
    // page emits — escapes match the Next.js App Router payload format the
    // parser walks. Only the fields the parser reads are populated; the rest
    // of the page (nav, footer, CSS) is omitted as the parser ignores it.
    private static string BuildDailyPageHtml(
        string totalRatio,
        string equityRatio,
        string indexRatio,
        string vixRatio,
        string etpRatio,
        long totalCall,
        long totalPut,
        long totalVolume,
        long equityCall,
        long equityPut,
        long equityVolume
    )
    {
        var optionsDataJson =
            "{"
            + "\"ratios\":["
            + $"{{\"name\":\"TOTAL PUT/CALL RATIO\",\"value\":\"{totalRatio}\"}},"
            + $"{{\"name\":\"INDEX PUT/CALL RATIO\",\"value\":\"{indexRatio}\"}},"
            + $"{{\"name\":\"EXCHANGE TRADED PRODUCTS PUT/CALL RATIO\",\"value\":\"{etpRatio}\"}},"
            + $"{{\"name\":\"EQUITY PUT/CALL RATIO\",\"value\":\"{equityRatio}\"}},"
            + $"{{\"name\":\"CBOE VOLATILITY INDEX (VIX) PUT/CALL RATIO\",\"value\":\"{vixRatio}\"}}"
            + "],"
            + $"\"SUM OF ALL PRODUCTS\":[{{\"name\":\"VOLUME\",\"call\":{totalCall},\"put\":{totalPut},\"total\":{totalVolume}}}],"
            + $"\"INDEX OPTIONS\":[{{\"name\":\"VOLUME\",\"call\":0,\"put\":0,\"total\":0}}],"
            + $"\"EXCHANGE TRADED PRODUCTS\":[{{\"name\":\"VOLUME\",\"call\":0,\"put\":0,\"total\":0}}],"
            + $"\"EQUITY OPTIONS\":[{{\"name\":\"VOLUME\",\"call\":{equityCall},\"put\":{equityPut},\"total\":{equityVolume}}}],"
            + $"\"CBOE VOLATILITY INDEX (VIX)\":[{{\"name\":\"VOLUME\",\"call\":0,\"put\":0,\"total\":0}}]"
            + "}";

        var escaped = optionsDataJson.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return "<html><body><script>"
            + "self.__next_f.push([1,\"...\\\"optionsData\\\":"
            + escaped
            + ",\\\"selectedDate\\\":\\\"2026-05-26\\\"...\"])"
            + "</script></body></html>";
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses;
        public List<HttpRequestMessage> Requests { get; } = new();

        public ScriptedHandler(params (HttpStatusCode Status, string Body)[] responses)
        {
            _responses = new Queue<(HttpStatusCode, string)>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    "ScriptedHandler exhausted — retry loop made more calls than expected."
                );
            }
            var (status, body) = _responses.Dequeue();
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body) }
            );
        }
    }
}
