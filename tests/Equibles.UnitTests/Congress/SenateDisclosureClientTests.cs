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
    public void ParseReportRow_RowWithUnparseableDate_ReturnsNullInsteadOfThrowing() {
        // Fifth pin in the ParseReportRow family. Existing pins cover:
        //   • cross-origin absolute URL → null
        //   • paper-filing URL → null
        //   • no-anchor link cell → null
        //   • valid row → returns SenateReport (happy path)
        // The unparseable-date branch is still unpinned:
        //   if (!DateOnly.TryParse(row[4]?.Trim(), out var dateSubmitted)) {
        //       _logger.LogDebug("Skipping Senate report with unparseable date: {Date}", row[4]);
        //       return null;
        //   }
        //
        // Senate eFD JSON occasionally emits rows with non-ISO date strings (legacy
        // exports use mm/dd/yyyy with culture-dependent ordering, or pure placeholder
        // strings like "Pending" for in-progress filings). The TryParse guard is the
        // boundary between "skip this row" and "crash the whole search loop." A
        // refactor that "modernizes" the code to `DateOnly.Parse(row[4].Trim())`
        // would throw FormatException on the first bad date, propagate out of
        // SearchPtrReports, and abort the entire Senate ingest for the day.
        //
        // The risk asymmetry: every existing rejection pin has a VALID date column
        // (the malformed bit is the URL or anchor). None exercises the date-parse
        // branch. Pin with a clearly-unparseable value ("not-a-date") so a regex/
        // culture-flexibility regression doesn't accidentally parse it.
        var sut = new SenateDisclosureClient(Substitute.For<ILogger<SenateDisclosureClient>>());
        var row = new List<string> {
            "Jane",
            "Doe",
            "filed",
            "<a href=\"/search/view/ptr/abc-123/\">link</a>",
            "not-a-date",
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

    [Fact]
    public void ParseReportRow_RowWithFewerThanFiveColumns_ReturnsNullInsteadOfIndexOutOfRange() {
        // Seventh pin in the ParseReportRow rejection family. The method opens with
        //   if (row.Count < 5) return null;
        // — a load-bearing length guard before the subsequent row[0]/row[1]/row[3]/row[4]
        // indexer accesses. Every other rejection sibling in this file passes a row
        // with exactly 5 elements; none of them exercises the short-row guard.
        //
        // The risk this catches: Senate eFD's JSON DataTables response occasionally
        // emits rows with fewer than 5 elements — placeholder rows for withdrawn
        // filings, aggregator summary rows, or column-schema changes during an
        // eFD UI rollout (the search API has shipped 4-col and 6-col variants
        // historically). Without the guard, row[4] throws ArgumentOutOfRangeException,
        // which propagates out of ParseReportRow through SearchPtrReports' foreach,
        // out of GetRecentTransactions, into CongressionalTradeSyncService's
        // FetchSenateTransactions catch — and aborts the entire Senate sync for the
        // day. The next day's run finds the same short row at the same offset and
        // crashes again. Operators see "Senate disclosure data failed to fetch"
        // warnings stack up with no apparent fix until someone reads the stack
        // trace.
        //
        // A refactor that "simplifies" the guard — e.g. someone reading the method
        // and assuming the parameter is always well-formed because the upstream
        // JSON parse "should" produce 5-col rows — would compile cleanly, pass
        // all six existing ParseReportRow pins (every one has Count == 5), and
        // silently reintroduce the IndexOutOfRangeException crash path. Pin the
        // short-row case with a deliberately 4-element row so the guard can't
        // be removed without a test failure.
        //
        // Asserting NotThrow + null distinguishes a working guard from a refactor
        // that lets the indexer throw: a missing guard would surface as
        // TargetInvocationException wrapping IndexOutOfRangeException, which
        // .Should().BeNull() can't observe (it asserts on a return value the
        // exception prevents from existing).
        var sut = new SenateDisclosureClient(Substitute.For<ILogger<SenateDisclosureClient>>());
        var row = new List<string> {
            "Jane",
            "Doe",
            "filed",
            "<a href=\"/search/view/ptr/abc-123/\">link</a>",
        };

        var act = () => ParseReportRowMethod.Invoke(sut, [row]);

        act.Should().NotThrow();
        ParseReportRowMethod.Invoke(sut, [row]).Should().BeNull();
    }

    [Fact]
    public void ParseReportRow_BothFirstAndLastNameEmpty_ReturnsNull() {
        // Sixth pin in the ParseReportRow rejection family. Covers the empty-name
        // guard: `if (string.IsNullOrEmpty(memberName)) return null;` where
        // memberName = $"{firstName} {lastName}".Trim().
        //
        // Senate eFD JSON occasionally returns rows with blank firstName AND
        // blank lastName — corruption from upstream redaction, withdrawn filings
        // pending re-publish, or aggregator-style rows representing summary
        // counts rather than individual reports. The guard prevents these from
        // producing a SenateReport whose MemberName is empty, which would
        // cascade into FetchAndParseReport → ParseTransactionsFromHtml with
        // an empty member-name string, persisting transactions tagged to no
        // identifiable person (breaking the "trades by member" filter in the
        // dashboard).
        //
        // The risk this catches: a refactor that drops the IsNullOrEmpty
        // check on memberName (e.g. assuming Senate eFD always sends names)
        // would pass every existing rejection sibling — none of them
        // exercises this branch — and silently create phantom reports tagged
        // to an empty member. Pin with both first AND last name as empty
        // strings (the JSON-likely shape; nulls are also possible but ?.Trim
        // on "" is the same as ?.Trim on null after the ?? "" coalesce).
        var sut = new SenateDisclosureClient(Substitute.For<ILogger<SenateDisclosureClient>>());
        var row = new List<string> {
            "",
            "",
            "filed",
            "<a href=\"/search/view/ptr/abc-123/\">link</a>",
            "2024-01-15",
        };

        var result = ParseReportRowMethod.Invoke(sut, [row]);

        result.Should().BeNull();
    }
}
