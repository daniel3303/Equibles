using System.Net;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Models;
using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Cboe;

public class CboeClientTests
{
    private const string PutCallBaseUrl =
        "https://cdn.cboe.com/resources/options/volume_and_call_put_ratios";
    private const string VixUrl =
        "https://cdn.cboe.com/api/global/us_indices/daily_prices/VIX_History.csv";

    [Theory]
    [InlineData(CboePutCallCsvType.Total, "totalpc.csv")]
    [InlineData(CboePutCallCsvType.Equity, "equitypc.csv")]
    [InlineData(CboePutCallCsvType.Index, "indexpc.csv")]
    [InlineData(CboePutCallCsvType.Vix, "vixpc.csv")]
    [InlineData(CboePutCallCsvType.Etp, "etppc.csv")]
    public async Task DownloadPutCallRatios_EachCsvType_FetchesMatchingCdnFileAndReturnsParsedRecords(
        CboePutCallCsvType csvType,
        string expectedFileName
    )
    {
        // The CsvFileNames dictionary maps each CboePutCallCsvType to its CDN file name.
        // A swapped or typo'd entry would point one consumer (e.g. Total ratios) at
        // another file's data (e.g. Equity), and the import would silently land wrong
        // values under the wrong type. Pin every enum->file mapping end-to-end.
        var csv =
            "Date,Call Volume,Put Volume,Total Volume,P/C Ratio\n"
            + "01/15/2025,100000,80000,200000,0.80\n";
        var handler = new ScriptedHandler((HttpStatusCode.OK, csv));
        var sut = CreateSut(handler);

        var result = await sut.DownloadPutCallRatios(csvType);

        handler
            .Requests.Should()
            .ContainSingle()
            .Which.RequestUri!.ToString()
            .Should()
            .Be($"{PutCallBaseUrl}/{expectedFileName}");
        result.Should().ContainSingle().Which.Date.Should().Be(new DateOnly(2025, 1, 15));
    }

    [Fact]
    public async Task DownloadVixHistory_OkResponse_FetchesVixHistoryUrlAndReturnsParsedRecords()
    {
        // Pin the VIX endpoint URL and confirm the full DownloadVixHistory -> ParseVixCsv
        // flow on a well-formed feed. The constant VixUrl is hardcoded; a typo or
        // a future change to a different CBOE CDN path would silently break the
        // VIX ingest in production. Asserting on the requested URL fixes the contract.
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
        // 429 path: the loop must back off (RateLimiter.PauseFor + Task.Delay) and
        // try again, returning the eventual success body. Without the retry the
        // first CBOE 429 of the day would abort the whole ingest. The initial
        // delay is 2^(0+1) = 2s, so this test pays a real 2s wait — keep it to
        // a single retry. The success on attempt #2 also proves the loop EXITS
        // the retry branch instead of looping forever. The fake limiter records
        // PauseFor so we can assert the 429 path called it (vs the 5xx path).
        var csv =
            "Date,Call Volume,Put Volume,Total Volume,P/C Ratio\n"
            + "01/15/2025,100000,80000,200000,0.80\n";
        var handler = new ScriptedHandler(
            (HttpStatusCode.TooManyRequests, ""),
            (HttpStatusCode.OK, csv)
        );
        var rateLimiter = Substitute.For<IRateLimiter>();
        var sut = CreateSut(handler, rateLimiter);

        var result = await sut.DownloadPutCallRatios(CboePutCallCsvType.Total);

        handler.Requests.Should().HaveCount(2);
        result.Should().ContainSingle();
        rateLimiter.Received(1).PauseFor(Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task DownloadWithRetry_ServerErrorThenOk_RetriesAndReturnsParsedContent()
    {
        // 5xx path is structurally distinct from the 429 path: it does NOT call
        // RateLimiter.PauseFor (server errors aren't a rate-limit signal), only
        // Task.Delay. Pin the retry-on-5xx -> success flow so a refactor that
        // collapses both branches into one or drops the 5xx retry is caught.
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

        var act = async () => await sut.DownloadPutCallRatios(CboePutCallCsvType.Total);

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.Requests.Should().ContainSingle();
    }

    private static CboeClient CreateSut(ScriptedHandler handler, IRateLimiter rateLimiter = null) =>
        new(
            new HttpClient(handler),
            Substitute.For<ILogger<CboeClient>>(),
            rateLimiter ?? Substitute.For<IRateLimiter>()
        );

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
