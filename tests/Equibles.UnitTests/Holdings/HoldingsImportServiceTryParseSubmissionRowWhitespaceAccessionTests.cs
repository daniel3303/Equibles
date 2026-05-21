using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceTryParseSubmissionRowWhitespaceAccessionTests
{
    // TryParseSubmissionRow gates the ACCESSION_NUMBER field with
    // `string.IsNullOrEmpty(accession)` — catching null and "" but letting
    // whitespace-only "   " through. Per the strict-null-on-malformed
    // precedent for sibling parse helpers (GH-1350 IsRecentFtdFile, GH-1438
    // FtdImportService.ParseLine, GH-1514 TryBuildParsedFact, GH-1544
    // TryParseOtherManagerRow), a SEC SUBMISSION.tsv row carrying a blanked
    // ACCESSION_NUMBER is malformed and should be rejected — not surfaced
    // downstream where it lands in the Submissions dictionary keyed by a
    // whitespace string and propagates through every join (COVERPAGE, FILER
    // mapping, INFOTABLE).
    [Fact]
    public void TryParseSubmissionRow_WhitespaceOnlyAccessionNumber_ReturnsFalse()
    {
        var minReportDate = new DateOnly(2024, 1, 1);
        var row = new Dictionary<string, string>
        {
            ["SUBMISSIONTYPE"] = "13F-HR",
            ["ACCESSION_NUMBER"] = "   ",
            ["PERIODOFREPORT"] = "2024-09-30",
            ["FILING_DATE"] = "2024-11-15",
            ["CIK"] = "1067983",
        };

        var method = typeof(HoldingsImportService).GetMethod(
            "TryParseSubmissionRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var args = new object[] { row, minReportDate, null };

        var resolved = (bool)method.Invoke(null, args);

        resolved.Should().BeFalse();
    }
}
