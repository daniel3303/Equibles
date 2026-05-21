using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceDeduplicateSubmissionsWhitespaceCikTests
{
    // DeduplicateSubmissions filters out submissions that cannot be reliably
    // grouped by (Cik, PeriodOfReport) — the existing
    // DeduplicateSubmissions_MissingCikOrPeriod_SkippedFromGrouping test pins
    // that null/empty Cik or PeriodOfReport short-circuit grouping so unrelated
    // filings are not falsely collapsed onto one another. Per the project's
    // strict-null-on-malformed precedent for the SUBMISSION.tsv ingest path
    // (GH-1350 IsRecentFtdFile, GH-1438 FtdImportService.ParseLine, GH-1514
    // TryBuildParsedFact, GH-1544 TryParseOtherManagerRow, GH-1563
    // TryParseSubmissionRow), a whitespace-only Cik is malformed: SEC CIKs are
    // numeric and can never legitimately be whitespace. The dedup filter must
    // treat whitespace identically to null/empty — otherwise two submissions
    // with Cik="   " and the same PeriodOfReport collapse into one and a
    // legitimate filing disappears.
    [Fact]
    public void DeduplicateSubmissions_WhitespaceOnlyCik_SkippedFromGrouping()
    {
        var context = new ImportContext
        {
            Submissions = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase)
            {
                ["ACC-001"] = new()
                {
                    AccessionNumber = "ACC-001",
                    Cik = "   ",
                    PeriodOfReport = "2024-09-30",
                    FilingDate = "2024-11-15",
                    FormType = "13F-HR",
                },
                ["ACC-002"] = new()
                {
                    AccessionNumber = "ACC-002",
                    Cik = "   ",
                    PeriodOfReport = "2024-09-30",
                    FilingDate = "2024-11-20",
                    FormType = "13F-HR",
                },
            },
        };

        HoldingsImportService.DeduplicateSubmissions(context);

        context.Submissions.Should().HaveCount(2);
    }
}
