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
