using System.Reflection;
using Equibles.Integrations.Sec;

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
