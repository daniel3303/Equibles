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
