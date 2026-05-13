using System.Reflection;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Tests for <see cref="SenateDisclosureClient"/>. Its public entry point drives a real
/// Playwright browser against efdsearch.senate.gov, so we exercise the pure-logic
/// private row-parser via reflection.
/// </summary>
public class SenateDisclosureClientTests {
    private static readonly MethodInfo ParseReportRowMethod = typeof(SenateDisclosureClient)
        .GetMethod("ParseReportRow", BindingFlags.NonPublic | BindingFlags.Instance);

    [Fact]
    public void ParseReportRow_AbsoluteUrlOutsideSenateBase_IsRejectedAndReturnsNull() {
        // The Senate disclosure feed delivers report links inside row HTML. Most are
        // relative paths (`/search/view/...`) that ParseReportRow prefixes with BaseUrl,
        // but the parser also accepts already-absolute URLs (`reportPath.StartsWith("http")`).
        // That branch trusts the source HTML's domain — an attacker who could feed the
        // importer a row whose href pointed at `https://evil.com/phish` would otherwise
        // get downstream code calling Playwright + the HTTP client against an arbitrary
        // origin, exfiltrating any cookie or credential the runtime might attach.
        // The guard is `if (!IsValidDisclosureUrl(reportUrl, BaseUrl)) return null;`
        // — drop it (or weaken the prefix check) and that cross-origin call goes through.
        // Pin the rejection on an attacker-controlled absolute URL so a refactor that
        // collapses the if/else into "always prepend BaseUrl" (which would silently
        // mangle absolute URLs but ALSO accept them) is caught.
        var sut = new SenateDisclosureClient(Substitute.For<ILogger<SenateDisclosureClient>>());
        var row = new List<string> {
            "Jane",
            "Doe",
            "filed",
            "<a href=\"https://evil.example/phish/report\">link</a>",
            "2024-01-15",
        };

        var result = ParseReportRowMethod.Invoke(sut, [row]);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseReportRow_ValidRowWithRelativeUrl_ReturnsReportWithBaseUrlPrepended() {
        // The two existing pins (cross-origin absolute URL, paper-filing path) are
        // both REJECTION paths — they prove ParseReportRow refuses bad inputs. Neither
        // proves the method actually ACCEPTS well-formed inputs. A regression that
        // returned null unconditionally would compile cleanly, pass both rejection
        // siblings, and silently halve the Senate disclosure pipeline (every report
        // is rejected → reports list stays empty → no transactions imported). The
        // failure mode is invisible: no exception, no error log, just zero new
        // Senate trades appearing in the database.
        //
        // Pin the acceptance branch with the realistic shape Senate eFD actually
        // returns: a relative URL that must be prefixed with BaseUrl, a valid
        // submitted-date, and a non-empty first/last name. The assertion uses
        // dynamic property access because SenateReport is a private nested record
        // — the existing tests assert .Should().BeNull() which doesn't expose
        // SenateReport's surface, so dynamic is the minimal-surface way to
        // assert on the returned report's fields without leaking the private
        // type into test code.
        //
        // The three field assertions cover the three derivations the method
        // performs: name composition (first + " " + last), URL prefixing
        // (BaseUrl + relative path), and date parsing (DateOnly.TryParse).
        // Together with the two rejection siblings, ParseReportRow's contract
        // is fully pinned: rejects cross-origin, rejects paper, accepts valid
        // electronic Senate filings.
        var sut = new SenateDisclosureClient(Substitute.For<ILogger<SenateDisclosureClient>>());
        var row = new List<string> {
            "Jane",
            "Doe",
            "filed",
            "<a href=\"/search/view/ptr/abc-123/\">link</a>",
            "2024-01-15",
        };

        var result = ParseReportRowMethod.Invoke(sut, [row]);

        result.Should().NotBeNull();
        var resultType = result.GetType();
        ((string)resultType.GetProperty("MemberName").GetValue(result)).Should().Be("Jane Doe");
        ((string)resultType.GetProperty("ReportUrl").GetValue(result))
            .Should().Be("https://efdsearch.senate.gov/search/view/ptr/abc-123/");
        ((DateOnly)resultType.GetProperty("DateSubmitted").GetValue(result))
            .Should().Be(new DateOnly(2024, 1, 15));
    }

    [Fact]
    public void ParseReportRow_LinkCellWithoutAnchorTag_ReturnsNullInsteadOfFakeReportPointingAtBaseUrl() {
        // ParseReportRow extracts the report URL from row[3] using
        //   var hrefMatch = HrefRegex().Match(linkHtml);
        //   if (!hrefMatch.Success) return null;
        // Without the .Success guard, the next line `var reportPath = hrefMatch.Groups[1].Value;`
        // would silently return an empty string (Match.Groups[1] on a failed match is a
        // valid empty Group, not null). reportPath="" then composes a report URL of just
        // `BaseUrl + ""` = BaseUrl, and IsValidDisclosureUrl(BaseUrl, BaseUrl) returns
        // true since BaseUrl trivially starts with itself. The downstream FetchAndParseReport
        // would then fetch the Senate eFD HOME PAGE as if it were a transaction report and
        // try to parse the empty result table. The failure mode is subtle: no exception,
        // no crash, just every "broken-link" Senate row producing a phantom report whose
        // FetchAndParseReport call wastes a Playwright round-trip against the home page
        // and inflates the error log with parse failures that look like upstream changes.
        //
        // Senate eFD JSON occasionally emits rows where row[3] carries plain text rather
        // than an `<a href=...>` tag (placeholder rows for withdrawn filings, legal
        // holds, or rows blocked by the eFD's own redaction layer). The HrefRegex
        // pattern `href=[""']([^""']+)[""']` simply doesn't match when no href= literal
        // is present.
        //
        // The two existing rejection siblings (paper-filing path, cross-origin absolute
        // URL) both have a valid <a href=...> tag. Neither exercises the .Success guard.
        // Pin the no-anchor case with a plain-text link cell so a refactor that drops
        // the guard surfaces here rather than as silent home-page fetches in production.
        var sut = new SenateDisclosureClient(Substitute.For<ILogger<SenateDisclosureClient>>());
        var row = new List<string> {
            "John",
            "Doe",
            "filed",
            "no anchor tag here — placeholder text only",
            "2024-01-15",
        };

        var result = ParseReportRowMethod.Invoke(sut, [row]);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseReportRow_PaperFilingUrl_ReturnsNull() {
        // Senate disclosures come in two flavours: HTML electronic filings (parseable) and
        // scanned-PDF "paper" filings (unparseable). ParseReportRow skips paper filings by
        // looking for "/view/paper/" in the report URL. If a regression dropped the check
        // (or used a wrong path segment), the importer would forward scanned PDFs into
        // FetchAndParseReport, fail mid-flight, and either flood the error log or crash
        // the scrape. Pin the skip so a refactor can't quietly let paper filings through.
        var sut = new SenateDisclosureClient(Substitute.For<ILogger<SenateDisclosureClient>>());
        var row = new List<string> {
            "John",
            "Doe",
            "filed",
            "<a href=\"/search/view/paper/abc-123/\">link</a>",
            "2024-01-15",
        };

        var result = ParseReportRowMethod.Invoke(sut, [row]);

        result.Should().BeNull();
    }
}
