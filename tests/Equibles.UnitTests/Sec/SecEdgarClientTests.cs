using System.Net.Http.Headers;
using System.Reflection;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Tests for <see cref="SecEdgarClient"/>. The public surface drives real HTTP calls
/// against SEC EDGAR; we exercise the pure-logic private static URL helpers via
/// reflection — same pattern as YahooFinanceClientTests and CftcClientTests.
/// </summary>
public class SecEdgarClientTests {
    private static readonly MethodInfo FormatCikMethod = typeof(SecEdgarClient)
        .GetMethod("FormatCik", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo GetDocumentUrlMethod = typeof(SecEdgarClient)
        .GetMethod("GetDocumentUrl", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo GetRetryDelayMethod = typeof(SecEdgarClient)
        .GetMethod("GetRetryDelay", BindingFlags.NonPublic | BindingFlags.Instance);

    [Fact]
    public void GetRetryDelay_RetryAfterDeltaLongerThanMaxRetryDelay_CapsAtMaxRetryDelay() {
        // SEC EDGAR's load balancer occasionally returns Retry-After headers asking
        // clients to back off for hours (e.g. during an outage SEC has sent values
        // like 3600s — 1 hour — and longer windows have been observed during the
        // EDGAR-modernization cutover). SendWithRetryAsync passes that delay verbatim
        // into Task.Delay; without a cap, a single bad upstream response would block
        // the entire scraper for a full hour (or longer), silently freezing every
        // dependent worker (DocumentScraper, FtdScraper, HoldingsScraper) that
        // shares this client.
        //
        // The cap is `private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);`
        // applied in GetRetryDelay via `return delta > MaxRetryDelay ? MaxRetryDelay : delta;`.
        // A refactor that "trusts SEC's Retry-After value" — dropping the cap or
        // raising the ceiling without bound — would compile cleanly and pass every
        // existing happy-path test (the existing tests don't exercise GetRetryDelay
        // at all). Pin the cap on a Retry-After value an order of magnitude over
        // the limit so any plausible regression surfaces; assert the returned
        // delay is exactly MaxRetryDelay (not the requested 24h delta).
        //
        // Construction: build a real HttpResponseMessage with a long Retry-After
        // header, build a minimal SecEdgarClient via its DI constructor (HttpClient,
        // NullLogger, IConfiguration with no ContactEmail — the logger warning is
        // acceptable here and doesn't fail the test), and invoke the private
        // instance method via reflection. The `attempt` parameter is irrelevant
        // when Retry-After is honoured — pass 0.
        using var httpClient = new HttpClient();
        var configuration = new ConfigurationBuilder().Build();
        var sut = new SecEdgarClient(httpClient, NullLogger<SecEdgarClient>.Instance, configuration);

        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(24));

        var delay = (TimeSpan)GetRetryDelayMethod.Invoke(sut, [response, 0]);

        delay.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void FormatCik_ShortCik_PadsLeftToTenDigitsWithZeros() {
        // SEC EDGAR's archive URLs require the CIK in a specific 10-digit
        // zero-padded form: `/Archives/edgar/data/{0000320193}/...` for
        // Apple's real CIK `320193`. The unpadded form (`320193`) returns
        // 404 from the CDN — SEC's load balancer rejects bare CIKs even
        // when they're numerically valid.
        //
        // The risk this pins: a refactor that swaps `PadLeft(10, '0')` for
        // any of the plausible "tidy-up" alternatives — `PadLeft(8, '0')`
        // (off-by-two from the 8-digit accession-number prefix elsewhere in
        // the file), `PadLeft(10, ' ')` (default char is space, easy to
        // miss when reviewing), or dropping the pad entirely under the
        // assumption that SEC accepts unpadded inputs — would compile
        // cleanly and pass every existing integration test whose fixture
        // CIK happens to already be 10 digits OR which uses HTTP-level
        // mocks that don't care about the URL format. Every real
        // SEC fetch against a sub-10-digit CIK (the majority — only the
        // largest registrants have CIKs ≥ 1_000_000_000) would silently
        // 404, the worker would log the error and continue, and the
        // filing pipeline would silently stall for those companies with
        // no CI signal.
        //
        // Pin the most realistic case: Apple's CIK `320193` padded to
        // `0000320193`. Asserting BOTH the digit count AND the leading
        // zeros distinguishes this from a `PadLeft(10, ' ')` regression
        // (which would yield "    320193") and a `PadLeft(6)` regression
        // (which would yield "320193").
        var result = (string)FormatCikMethod.Invoke(null, ["320193"]);

        result.Should().Be("0000320193");
    }

    [Fact]
    public void GetDocumentUrl_EmptyCik_ReturnsEmptyStringInsteadOfMalformedUrl() {
        // GetDocumentUrl's first line is the defensive guard
        //   `if (string.IsNullOrEmpty(cik) || string.IsNullOrEmpty(accessionNumber))
        //        return string.Empty;`
        // Without it, an empty CIK would flow into FormatCik("") which returns
        // "".PadLeft(10, '0') = "0000000000" — a SYNTACTICALLY VALID 10-digit
        // padded CIK that composes into the URL
        //   https://www.sec.gov/Archives/edgar/data/0000000000/...accession.txt
        // That URL hits SEC's CDN successfully (no exception), gets a 404 back,
        // and the caller treats it as a transient miss + retries. The failure
        // mode is the worst kind: looks like a missing filing rather than the
        // upstream null-CIK bug that produced it. Every existing test uses a
        // real Apple CIK ("320193"), so the empty/null guard branch is unpinned
        // and a refactor that drops it (e.g., "simplify away the defensive
        // check since CIKs come from a trusted DB column") would silently
        // shift the failure mode from "obvious empty URL" to "infinite retry
        // on a deceptive 404".
        //
        // Sibling to the existing GetDocumentUrl happy-path pin. Pair (valid
        // CIK → real URL, empty CIK → string.Empty) covers both arms of the
        // guard contract.
        var result = (string)GetDocumentUrlMethod.Invoke(null, ["", "0000320193-25-000001-index"]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetRetryDelay_RetryAfterDeltaWithinCap_ReturnsDeltaAsIsNotMaxRetryDelay() {
        // Sibling to GetRetryDelay_RetryAfterDeltaLongerThanMaxRetryDelay_CapsAtMaxRetryDelay.
        // That pin catches the cap branch (`delta > MaxRetryDelay ? MaxRetryDelay : delta`)
        // on a 24-hour Retry-After. This pin catches the OTHER side of the same ternary:
        // when SEC suggests a short backoff (30s), the helper must return that delta
        // verbatim — not the MaxRetryDelay ceiling.
        //
        // The risk this catches is asymmetric and the cap pin can't see it: a refactor
        // that simplified `return delta > MaxRetryDelay ? MaxRetryDelay : delta;` to just
        // `return MaxRetryDelay;` (e.g. "always cap" defensive simplification) would
        // still pass the existing cap test — the cap test's expected value IS
        // MaxRetryDelay. Every SEC 429 would then block for the full 5-minute cap even
        // when SEC explicitly suggests a 30-second backoff. The downstream effect is
        // 10× slowdown on every transient SEC throttle — invisible to log inspection
        // (no error, just sluggish throughput) and difficult to diagnose without this
        // test.
        //
        // Pair (cap pin + delta-as-is pin) covers both ternary arms. Pick a realistic
        // small SEC suggestion (30 seconds) — SEC's actual 429 responses commonly
        // include Retry-After values between 5 and 60 seconds, well below the
        // 5-minute MaxRetryDelay.
        using var httpClient = new HttpClient();
        var configuration = new ConfigurationBuilder().Build();
        var sut = new SecEdgarClient(httpClient, NullLogger<SecEdgarClient>.Instance, configuration);

        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));

        var delay = (TimeSpan)GetRetryDelayMethod.Invoke(sut, [response, 0]);

        delay.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void GetRetryDelay_NoRetryAfterHeader_UsesExponentialBackoffFormulaTwoToAttemptPlusOne() {
        // Third pin in the GetRetryDelay family. Existing pins cover the two
        // RetryAfter.Delta ternary arms (cap and within-cap). This pin covers
        // path 3: when the response has NO Retry-After header (SEC's 429s
        // routinely arrive without one), GetRetryDelay falls through both the
        // Delta and Date branches and lands on the exponential backoff:
        //   var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
        //   return backoff > MaxRetryDelay ? MaxRetryDelay : backoff;
        //
        // The formula `2^(attempt + 1)` is load-bearing — it yields the
        // intended backoff curve 2s, 4s, 8s, 16s, 32s, 64s, ... (capped at
        // 5min). A refactor that "simplified" to `Math.Pow(2, attempt)` (off
        // by one — yields 1s, 2s, 4s, 8s, ...) would silently halve every
        // fallback delay, hammering SEC's rate limiter 2× as fast during
        // outages and inviting longer bans. The change would compile cleanly,
        // pass both existing GetRetryDelay pins (those use RetryAfter and
        // never hit this code path), and only surface as production-load
        // anomalies during the next SEC throttling event.
        //
        // Pick attempt=2 specifically:
        //   • attempt=0 → 2^1 = 2s
        //   • attempt=1 → 2^2 = 4s
        //   • attempt=2 → 2^3 = 8s   ← this pin
        //   • attempt=10 → 2^11 = 2048s → capped to 300s
        // 8s is well below the cap (so the cap clause is a no-op here) and
        // distinct enough from neighbouring attempts that a single-position
        // off-by-one in the formula fails the assertion. Use no Retry-After
        // header (the production-default 429 shape from SEC).
        using var httpClient = new HttpClient();
        var configuration = new ConfigurationBuilder().Build();
        var sut = new SecEdgarClient(httpClient, NullLogger<SecEdgarClient>.Instance, configuration);

        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);

        var delay = (TimeSpan)GetRetryDelayMethod.Invoke(sut, [response, 2]);

        delay.Should().Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void GetDocumentUrl_ValidCikAndAccession_ComposesSecArchiveTxtUrlWithPaddedCik() {
        // Sibling to the FormatCik pin above. GetDocumentUrl composes the
        // exact URL the caller hands to HttpClient.GetAsync to fetch a
        // filing's raw text envelope. SEC's CDN matches the URL byte-for-
        // byte:
        //   - The domain MUST be www.sec.gov (not data.sec.gov — the
        //     submissions API uses data.sec.gov but the archive uses
        //     www.sec.gov, and BaseUrl in this client points at data.sec.gov,
        //     so a refactor that "deduplicates" the literal by routing
        //     through BuildUrl would silently produce 404s).
        //   - The path MUST be /Archives/edgar/data/{padded-cik}/{accession}
        //     in that exact case (SEC's CDN serves capitalized `Archives`
        //     but is case-sensitive — `/archives/...` 404s).
        //   - The extension MUST be `.txt` for the raw SGML envelope; SEC
        //     also publishes `.htm` index pages at adjacent URLs, but
        //     parsing those would mis-route to an HTML index document
        //     rather than the actual filing content.
        //   - The CIK must be zero-padded to 10 digits (covered indirectly
        //     here, redundant with the FormatCik pin; pair gives belt-and-
        //     suspenders coverage in case the FormatCik wiring is bypassed).
        //
        // The risk this catches is a "refactor that goes too far": someone
        // sees `https://www.sec.gov/...` as a magic string and tries to
        // hoist it to a constant or compose it via `BuildUrl(...)` — both
        // produce silently wrong URLs (BuildUrl uses BaseUrl =
        // data.sec.gov, and any constant-hoisting that picks the wrong
        // domain matches the SEC URL conventions but 404s in production).
        // Existing integration tests don't necessarily catch the URL
        // shape — most are HTTP-level mocks that match on relative path,
        // not the full absolute URL.
        //
        // Pin with Apple's CIK and a realistic accession number; assert
        // the literal output so a 1-character drift surfaces.
        var result = (string)GetDocumentUrlMethod.Invoke(null, ["320193", "0000320193-25-000001-index"]);

        result.Should().Be("https://www.sec.gov/Archives/edgar/data/0000320193/0000320193-25-000001-index.txt");
    }
}
