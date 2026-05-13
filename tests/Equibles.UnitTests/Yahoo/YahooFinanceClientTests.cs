using System.Reflection;
using Equibles.Integrations.Yahoo;

namespace Equibles.UnitTests.Yahoo;

public class YahooFinanceClientTests {
    private static readonly MethodInfo ToUnixTimestampMethod = typeof(YahooFinanceClient)
        .GetMethod("ToUnixTimestamp", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo FromUnixTimestampMethod = typeof(YahooFinanceClient)
        .GetMethod("FromUnixTimestamp", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo ApplyBrowserHeadersOnClientMethod = typeof(YahooFinanceClient)
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .First(m => m.Name == "ApplyBrowserHeaders" && m.GetParameters()[0].ParameterType == typeof(HttpClient));

    private static readonly MethodInfo ApplyBrowserHeadersOnRequestMethod = typeof(YahooFinanceClient)
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .First(m => m.Name == "ApplyBrowserHeaders" && m.GetParameters()[0].ParameterType == typeof(HttpRequestMessage));

    [Fact]
    public void ApplyBrowserHeaders_OnHttpRequestMessage_AddsChromeUserAgentAndAcceptHeaders() {
        // The HttpClient-overload sibling (covered in PR #268) attaches headers on
        // `DefaultRequestHeaders` of the session-bootstrap client — only used for
        // the cookie/crumb handshake. The HttpRequestMessage overload is what runs
        // on EVERY data-fetch hop inside SendWithRetry: the DI-injected HttpClient
        // does not carry `DefaultRequestHeaders` (it's reused across many integrations),
        // so each chart / quoteSummary request manually attaches the same browser
        // headers via this overload before SendAsync. A refactor that drops this
        // call (e.g. assumes `DefaultRequestHeaders` are inherited from the DI
        // factory) would still pass the bootstrap test that covers the HttpClient
        // overload, but every subsequent /v8/finance/chart and /v10/finance/quoteSummary
        // call would arrive without a real User-Agent and get silently 403'd by
        // Yahoo's bot filter — the SendWithRetry loop would then refresh the session
        // and retry until it exhausts MaxRetries, masking the root cause as
        // "Yahoo session expired". Pin the per-request attach so the regression
        // surfaces at the right layer.
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://query1.finance.yahoo.com/v8/finance/chart/AAPL");

        ApplyBrowserHeadersOnRequestMethod.Invoke(null, [request]);

        request.Headers.UserAgent.ToString().Should().StartWith("Mozilla/5.0");
        request.Headers.Accept.ToString().Should().Contain("text/html");
        request.Headers.AcceptLanguage.ToString().Should().Contain("en-US");
    }

    [Fact]
    public void ApplyBrowserHeaders_OnHttpClient_AddsChromeUserAgentAndAcceptHeaders() {
        // Yahoo Finance actively rejects requests that look like bots — `query1.finance.yahoo.com`
        // returns 401/403 without explanation when the User-Agent header is missing or
        // identifies as a typical HttpClient (".NET HttpClient/...") rather than a real
        // browser. `ApplyBrowserHeaders(HttpClient)` is the bot-detection bypass: it
        // attaches a Chrome User-Agent plus the Accept and Accept-Language headers a
        // browser would normally send, on `DefaultRequestHeaders` so EVERY subsequent
        // call shares them. The risk this pins: a refactor that drops any of the three
        // headers (or replaces the constant `BrowserUserAgent` with `nameof(YahooFinanceClient)`,
        // or removes the call from session bootstrap entirely) would silently 401 the
        // next session-refresh after the 30-minute cache expiry — with no test signal
        // because the unit tests around `GetHistoricalPrices` only exercise the
        // ArgumentException paths that never hit the network. Asserting on the
        // emitted header proves the bypass is wired correctly.
        var client = new HttpClient();

        ApplyBrowserHeadersOnClientMethod.Invoke(null, [client]);

        client.DefaultRequestHeaders.UserAgent.ToString().Should().StartWith("Mozilla/5.0");
        client.DefaultRequestHeaders.Accept.ToString().Should().Contain("text/html");
        client.DefaultRequestHeaders.AcceptLanguage.ToString().Should().Contain("en-US");
    }

    [Fact]
    public void ToUnixTimestamp_KnownUtcDate_ReturnsCorrectUnixSeconds() {
        // GetHistoricalPrices builds the chart URL with `?period1={ToUnixTimestamp(start)}`,
        // and Yahoo treats those parameters as Unix epoch seconds in UTC. ToUnixTimestamp
        // anchors the conversion by explicitly constructing the DateTime with
        // DateTimeKind.Utc — drop the kind and the subtraction against the UTC-offset
        // UnixEpoch silently uses the host's local timezone, shifting every requested
        // range by the local offset and pulling the wrong day's prices. Pin the UTC
        // anchor by asserting the exact epoch seconds for a known calendar date.
        var result = (long)ToUnixTimestampMethod.Invoke(null, [new DateOnly(2024, 1, 1)]);

        result.Should().Be(1704067200L);
    }

    [Fact]
    public void FromUnixTimestamp_KnownUnixSeconds_ReturnsUtcCalendarDate() {
        // The chart response carries one Unix-epoch-seconds timestamp per OHLCV row.
        // FromUnixTimestamp converts it back to a DateOnly via `UnixEpoch.AddSeconds(...).UtcDateTime`
        // — drop the `.UtcDateTime` accessor and the implicit `.LocalDateTime` conversion
        // would shift the calendar date by the host's timezone offset, so a price stamped
        // 2024-01-01 00:00 UTC by Yahoo would land on 2023-12-31 for any host west of UTC
        // (and vice versa). That mis-stamps every historical row by ±1 day, breaks
        // (CommonStockId, Date) dedup, and silently double-imports the boundary day.
        // Pin the UTC anchor on the reverse direction, complementing the ToUnixTimestamp
        // test that pins the forward direction.
        var result = (DateOnly)FromUnixTimestampMethod.Invoke(null, [1704067200L]);

        result.Should().Be(new DateOnly(2024, 1, 1));
    }


    [Fact]
    public async Task GetHistoricalPrices_NullTicker_ThrowsArgumentException() {
        var client = CreateClient();

        var act = () => client.GetHistoricalPrices(null, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetHistoricalPrices_EmptyTicker_ThrowsArgumentException() {
        var client = CreateClient();

        var act = () => client.GetHistoricalPrices("", new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetHistoricalPrices_WhitespaceTicker_ThrowsArgumentException() {
        var client = CreateClient();

        var act = () => client.GetHistoricalPrices("   ", new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetRecommendationTrends_NullTicker_ThrowsArgumentException() {
        var client = CreateClient();

        var act = () => client.GetRecommendationTrends(null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetRecommendationTrends_EmptyTicker_ThrowsArgumentException() {
        var client = CreateClient();

        var act = () => client.GetRecommendationTrends("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static YahooFinanceClient CreateClient() {
        return new YahooFinanceClient(
            new HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<YahooFinanceClient>.Instance);
    }
}
