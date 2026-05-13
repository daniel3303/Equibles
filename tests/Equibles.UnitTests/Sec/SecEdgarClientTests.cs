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

    [Fact]
    public void GetRetryDelay_RetryAfterAbsoluteDateInFuture_ReturnsWaitUntilThatDate() {
        // Fourth pin in the GetRetryDelay family. Existing pins cover the three
        // Delta paths (cap, within-cap, no header → exponential backoff). This
        // pin covers path 4: RFC 7231 §7.1.3 permits Retry-After to carry an
        // absolute HTTP-date in addition to delta-seconds, and SEC has been
        // observed serving both forms during throttling. The Date branch in
        // GetRetryDelay is unpinned — every existing test uses a TimeSpan
        // (Delta) form.
        //
        // The risk this catches is structurally distinct from the Delta
        // branch: a refactor that "simplifies" GetRetryDelay by dropping the
        //   if (response.Headers.RetryAfter?.Date is { } date) { ... }
        // block (under the false intuition that "we never use the Date form
        // anyway" — the existing test corpus would back that intuition) would
        // compile, pass all three Delta pins, and silently fall through to
        // exponential backoff every time SEC sends an absolute-date Retry-After.
        // The fallback isn't catastrophic (the worker still retries), but the
        // honored delay would be 2-4s instead of the seconds-until-the-stated-
        // resumption-time SEC explicitly requested — hammering SEC's rate
        // limiter exactly when it's asking us to wait, inviting longer bans.
        //
        // Tactical risks the Date branch additionally guards:
        //   • Past-date handling: if SEC sends a stale Retry-After (clock skew,
        //     or SEC's deployment writes one from the previous request), the
        //     branch's `wait > TimeSpan.Zero` guard avoids a negative delay.
        //     Not pinned here — that's a separate edge case.
        //   • Cap: the Date branch has its own MaxRetryDelay cap mirroring the
        //     Delta branch's cap. Not pinned here — covered by symmetry with
        //     the existing Delta cap pin if the branch is preserved.
        //
        // The minimum the Date branch must do is the happy path: future date
        // within the cap → return ≈ (date - now). Pin a date ~2 minutes in
        // the future (well below MaxRetryDelay = 5min) so the cap clause is
        // a no-op and the assertion isolates the "return wait" line. A small
        // tolerance (BeCloseTo ±5s) absorbs the clock drift between
        // constructing the header and reading UtcNow inside GetRetryDelay —
        // that's the dominant non-determinism here.
        using var httpClient = new HttpClient();
        var configuration = new ConfigurationBuilder().Build();
        var sut = new SecEdgarClient(httpClient, NullLogger<SecEdgarClient>.Instance, configuration);

        var retryAt = DateTimeOffset.UtcNow.AddMinutes(2);
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAt);

        var delay = (TimeSpan)GetRetryDelayMethod.Invoke(sut, [response, 0]);

        delay.Should().BeCloseTo(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetRetryDelay_RetryAfterAbsoluteDateInPast_FallsThroughToExponentialBackoff() {
        // Sibling to `GetRetryDelay_RetryAfterAbsoluteDateInFuture_ReturnsWaitUntilThatDate`.
        // The future-date pin covers the Date-branch happy path. This pin
        // covers the SAFETY GUARD inside that branch:
        //   if (response.Headers.RetryAfter?.Date is { } date) {
        //       var wait = date - DateTimeOffset.UtcNow;
        //       if (wait > TimeSpan.Zero) {   ← THIS GUARD
        //           return wait > MaxRetryDelay ? MaxRetryDelay : wait;
        //       }
        //   }
        // The `wait > TimeSpan.Zero` check is load-bearing defensive code
        // against TWO real production conditions:
        //
        //   1. Clock skew. The runtime's UtcNow may drift past SEC's
        //      stated Retry-After date by a few hundred milliseconds —
        //      enough to produce a tiny negative wait. Without the
        //      guard, `Task.Delay(negativeTimeSpan)` would throw
        //      ArgumentOutOfRangeException at the caller, crashing the
        //      worker on every clock-skewed Retry-After response.
        //   2. Stale Retry-After. SEC's CDN occasionally re-serves a
        //      cached 429 response whose Retry-After header was written
        //      minutes or hours ago, so the date is firmly in the past.
        //      Falling through to the exponential-backoff path is the
        //      correct behavior — back off as if no Retry-After were
        //      sent.
        //
        // The risk this catches: a refactor that drops the
        // `wait > TimeSpan.Zero` guard — under the false intuition
        // that "SEC always sends future dates" — would compile, pass
        // the future-date sibling, and immediately crash the worker on
        // the first clock-skewed or stale-cached Retry-After response.
        //
        // Without this pin AND with the guard dropped, the fall-through
        // path is the exponential backoff (path 3 in the family). Pin
        // a past date and assert the result equals the expected
        // exponential value at attempt=2 (2^(2+1) = 8 seconds), which
        // proves:
        //   • Past-date check fired (didn't return negative wait).
        //   • Date branch's wait-guard correctly fell through.
        //   • Exponential backoff branch handled the fallback.
        // Use 1 day in the past — clearly outside any drift envelope so
        // the test isn't flaky on slow CI.
        using var httpClient = new HttpClient();
        var configuration = new ConfigurationBuilder().Build();
        var sut = new SecEdgarClient(httpClient, NullLogger<SecEdgarClient>.Instance, configuration);

        var retryAt = DateTimeOffset.UtcNow.AddDays(-1);
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAt);

        var delay = (TimeSpan)GetRetryDelayMethod.Invoke(sut, [response, 2]);

        delay.Should().Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void FilterFilings_DocumentTypeSpecified_ReturnsOnlyMatchingFormViaGetFormNameMapping() {
        // First pin in the FilterFilings family. SecEdgarClient.FilterFilings is the
        // private static three-clause AND filter that GetCompanyFilings applies to
        // SEC's per-CIK filing list:
        //   return filings.Where(f =>
        //       (!documentType.HasValue || f.Form == documentType.Value.GetFormName()) &&
        //       (!fromDate.HasValue || f.FilingDate >= fromDate.Value) &&
        //       (!toDate.HasValue || f.FilingDate <= toDate.Value)
        //   ).ToList();
        // The DocumentScraper layer composes this filter for every (company, form-type)
        // pair it sweeps — every 10-K, 10-Q, 8-K, 20-F, 6-K, 40-F, and Form 3/4 sweep
        // round-trips through this single line of code. A regression here silently
        // breaks the entire SEC ingest layer.
        //
        // Existing pins in this file cover GetRetryDelay, FormatCik, and GetDocumentUrl.
        // None exercises FilterFilings — the three private-static parsers
        // (ParseCompaniesFromResponse, MapToFilingData, FilterFilings) at the bottom
        // of the file are uncovered by every existing unit test.
        //
        // The risks this pin catches:
        //
        //   • Comparison-operator regression: `f.Form == documentType.Value.GetFormName()`
        //     swapped for `!=` would invert the filter, returning EVERY filing EXCEPT
        //     the requested form type. The DocumentScraper would skip every 10-K row
        //     while flooding the database with 6-K / 4 / 8-K rows it didn't ask for —
        //     compounded across every CIK sweep. The data corruption is invisible
        //     past the point a per-CIK row count check would catch (which the
        //     scraper doesn't do).
        //
        //   • Short-circuit-arm inversion: `!documentType.HasValue` flipped to
        //     `documentType.HasValue` would silently REJECT every filing when no
        //     filter is applied (the GetCompanyFilings overload with documentType=null
        //     would return empty). DocumentScraper's "fetch all forms" path would
        //     silently halt.
        //
        //   • Drop GetFormName: replacing `documentType.Value.GetFormName()` with
        //     `documentType.Value.ToString()` would compile cleanly — `ToString()`
        //     on the enum returns "TenK" while `f.Form` is "10-K". Every filing
        //     would be filtered out because no Form string ever equals "TenK". This
        //     is the SEC-display-name vs C#-identifier asymmetry the GetFormName
        //     extension was specifically introduced to handle.
        //
        // The existing GetFormName method (`DocumentTypeExtensions`) maps each
        // DocumentTypeFilter enum value through its [Display(Name = "...")]
        // attribute, so:
        //   • TenK   → "10-K"
        //   • TenQ   → "10-Q"
        //   • EightK → "8-K"
        //   • FormFour → "4"
        //   ...
        // The Form column in SEC's response uses the display-name form ("10-K",
        // not "TenK"). Without GetFormName(), the comparison degenerates to
        // mis-cased C# enum identifiers.
        //
        // Construction: build a mixed-form filing list (10-K, 10-Q, 8-K) and
        // call FilterFilings with documentType=TenK. Assert ONLY the 10-K
        // filing is returned (not the 10-Q with the substring overlap, not
        // the 8-K with a different prefix). The single-record assertion
        // distinguishes:
        //   • Working ==: returns the one 10-K filing.
        //   • Inverted !=: returns the 10-Q + 8-K (2 records, neither matching).
        //   • Drop GetFormName: returns 0 records (no Form == "TenK").
        //   • Drop the whole clause: returns all 3 records.
        //
        // Use reflection on the private static method, matching the style of
        // the other test helpers in this file. Pass empty AccessionNumber/Cik
        // strings — only Form matters for this assertion.
        var filterFilingsMethod = typeof(SecEdgarClient)
            .GetMethod("FilterFilings", BindingFlags.NonPublic | BindingFlags.Static);
        var filings = new List<Equibles.Integrations.Sec.Models.FilingData> {
            new() { Form = "10-K", AccessionNumber = "0001-10K", Cik = "320193" },
            new() { Form = "10-Q", AccessionNumber = "0002-10Q", Cik = "320193" },
            new() { Form = "8-K",  AccessionNumber = "0003-8K",  Cik = "320193" }
        };

        var result = (List<Equibles.Integrations.Sec.Models.FilingData>)filterFilingsMethod.Invoke(
            null,
            [filings, Equibles.Integrations.Sec.Models.DocumentTypeFilter.TenK, null, null]);

        result.Should().ContainSingle()
            .Which.Form.Should().Be("10-K");
    }

    [Fact]
    public void FilterFilings_FromDateSpecified_ExcludesBeforeAndIncludesOnOrAfterBoundary() {
        // Sibling pin to FilterFilings_DocumentTypeSpecified_…. FilterFilings has
        // three independent clauses joined by &&:
        //   (!documentType.HasValue || f.Form == documentType.Value.GetFormName()) &&
        //   (!fromDate.HasValue     || f.FilingDate >= fromDate.Value)             &&
        //   (!toDate.HasValue       || f.FilingDate <= toDate.Value)
        // The previous pin covers clause 1 (documentType + GetFormName). This one
        // covers clause 2 (fromDate + `>=`). They're independent — a regression in
        // the date comparison would slip past the documentType pin because that
        // pin passes `fromDate: null` and the short-circuit `!fromDate.HasValue`
        // returns true regardless of the comparison operator.
        //
        // The risks this pin uniquely catches:
        //
        //   • Operator swap `>=` → `>`: a "tighten the boundary" refactor under the
        //     (false) intuition that "filings ON the from-date should be excluded
        //     because the date is the day we last fetched" would compile cleanly,
        //     pass the documentType pin (fromDate=null, clause short-circuits), and
        //     silently drop every filing whose FilingDate equals the fromDate.
        //     DocumentScraper re-fetches with fromDate set to "last successfully
        //     imported date" — the boundary filings on that exact date are exactly
        //     the ones the importer must re-check for amendments. Dropping them
        //     silently loses amendment-of-day-N filings.
        //
        //   • Operator inversion `>=` → `<=`: less plausible but compiles. Would
        //     return only filings BEFORE the fromDate. The documentType pin doesn't
        //     see this because the fromDate clause is short-circuited there.
        //
        //   • Short-circuit-arm inversion: `!fromDate.HasValue` flipped to
        //     `fromDate.HasValue` makes the no-filter path return empty. Same risk
        //     class as the documentType pin's short-circuit, but specifically
        //     covers clause 2.
        //
        //   • Operand swap: `f.FilingDate >= fromDate.Value` swapped to
        //     `fromDate.Value >= f.FilingDate` produces the inverse filter.
        //
        // Construction: three-filing list with FilingDates spanning the boundary:
        //   • 2024-06-14 (BEFORE fromDate) → EXCLUDED.
        //   • 2024-06-15 (ON boundary)     → INCLUDED via `>=` (NOT `>`).
        //   • 2024-06-16 (AFTER)           → INCLUDED.
        // Working `>=`: 2 records. Strict `>`: 1 record (only 06-16). Inversion:
        // 1 record (only 06-14). Drop clause: 3 records. Short-circuit fail:
        // 0 records. All four regression classes distinguished by record count
        // and AccessionNumber identity.
        var filterFilingsMethod = typeof(SecEdgarClient)
            .GetMethod("FilterFilings", BindingFlags.NonPublic | BindingFlags.Static);
        var filings = new List<Equibles.Integrations.Sec.Models.FilingData> {
            new() { Form = "10-K", AccessionNumber = "0001", Cik = "320193",
                    FilingDate = new DateOnly(2024, 6, 14) },
            new() { Form = "10-K", AccessionNumber = "0002", Cik = "320193",
                    FilingDate = new DateOnly(2024, 6, 15) },
            new() { Form = "10-K", AccessionNumber = "0003", Cik = "320193",
                    FilingDate = new DateOnly(2024, 6, 16) },
        };
        DateOnly? fromDate = new DateOnly(2024, 6, 15);

        var result = (List<Equibles.Integrations.Sec.Models.FilingData>)filterFilingsMethod.Invoke(
            null,
            [filings, null, fromDate, null]);

        result.Should().HaveCount(2);
        result.Select(f => f.AccessionNumber).Should().BeEquivalentTo(["0002", "0003"]);
    }

    [Fact]
    public void FilterFilings_ToDateSpecified_ExcludesAfterAndIncludesOnOrBeforeBoundary() {
        // Final sibling in the FilterFilings clause-triple. The previous two pins
        // cover documentType + fromDate. This pin covers the third clause
        // (toDate + `<=`). With this pin the three-clause contract is
        // exhaustively pinned.
        //
        // The risks this pin uniquely catches (unreachable from the fromDate
        // sibling because the comparison directions are OPPOSITE):
        //
        //   • Operator swap `<=` → `<` (strict): drops on-boundary filings whose
        //     FilingDate equals the toDate. DocumentScraper's "fetch up to today"
        //     path uses `toDate = today`; with a strict `<` regression, every
        //     filing dated TODAY is silently skipped — the dominant on-cycle
        //     case for the freshest ingest. The fromDate sibling can't see
        //     this — it tests `>=` on the LOWER bound.
        //
        //   • Operator swap `<=` → `>=` (direction inversion): the toDate clause
        //     becomes "include only filings AFTER the toDate". Inverts the
        //     entire upper-bound logic.
        //
        //   • Operand swap: `f.FilingDate <= toDate.Value` swapped to
        //     `toDate.Value <= f.FilingDate` produces the inverse filter.
        //     Compiles cleanly.
        //
        //   • Short-circuit-arm inversion: `!toDate.HasValue` flipped to
        //     `toDate.HasValue` makes the no-filter path return empty.
        //
        // Construction: three filings spanning the upper boundary at 2024-06-15:
        //   • 2024-06-14 (BEFORE) → INCLUDED.
        //   • 2024-06-15 (ON)     → INCLUDED via `<=` (NOT `<`).
        //   • 2024-06-16 (AFTER)  → EXCLUDED.
        // Working `<=`: 2 records (06-14 + 06-15). Strict `<`: 1 record (06-14).
        // Inversion: 1 record (06-16). Drop clause: 3 records. Short-circuit
        // fail: 0 records.
        //
        // The pair (fromDate sibling + this pin) covers boundary inclusion in
        // BOTH directions — a refactor that flipped both operators symmetrically
        // would still fail one of the two pins, since the boundary filings are
        // at different dates and the asymmetric clause structure (`>=` vs `<=`)
        // distinguishes them.
        var filterFilingsMethod = typeof(SecEdgarClient)
            .GetMethod("FilterFilings", BindingFlags.NonPublic | BindingFlags.Static);
        var filings = new List<Equibles.Integrations.Sec.Models.FilingData> {
            new() { Form = "10-K", AccessionNumber = "0001", Cik = "320193",
                    FilingDate = new DateOnly(2024, 6, 14) },
            new() { Form = "10-K", AccessionNumber = "0002", Cik = "320193",
                    FilingDate = new DateOnly(2024, 6, 15) },
            new() { Form = "10-K", AccessionNumber = "0003", Cik = "320193",
                    FilingDate = new DateOnly(2024, 6, 16) },
        };
        DateOnly? toDate = new DateOnly(2024, 6, 15);

        var result = (List<Equibles.Integrations.Sec.Models.FilingData>)filterFilingsMethod.Invoke(
            null,
            [filings, null, null, toDate]);

        result.Should().HaveCount(2);
        result.Select(f => f.AccessionNumber).Should().BeEquivalentTo(["0001", "0002"]);
    }
}
