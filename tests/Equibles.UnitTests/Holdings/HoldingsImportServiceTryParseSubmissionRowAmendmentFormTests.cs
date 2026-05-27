using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceTryParseSubmissionRowAmendmentFormTests
{
    [Fact]
    public void TryParseSubmissionRow_AmendmentFormType13FHrSlashA_IsAccepted()
    {
        // TryParseSubmissionRow's form-type gate is
        //   `formType is not ("13F-HR" or "13F-HR/A") → return false`.
        // Every existing pin (Boundary, CutoffBoundary, WhitespaceAccession)
        // feeds the canonical "13F-HR" — none exercises the amendment
        // variant. A refactor that drops "13F-HR/A" from the pattern
        // (e.g. simplifying to `formType != "13F-HR"`) would compile,
        // pass every existing test, and silently drop every 13F
        // restatement from the import stream — holdings restatements
        // (which arrive as 13F-HR/A) would never reach the database,
        // and downstream consensus / dedup logic would still see the
        // superseded original. Pin: SUBMISSIONTYPE="13F-HR/A" parses
        // successfully and emits a SubmissionRow.
        var method = typeof(HoldingsImportService).GetMethod(
            "TryParseSubmissionRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var minReportDate = new DateOnly(2024, 1, 1);
        var row = new Dictionary<string, string>
        {
            ["SUBMISSIONTYPE"] = "13F-HR/A",
            ["ACCESSION_NUMBER"] = "0000950123-24-009999",
            ["PERIODOFREPORT"] = "2024-09-30",
            ["FILING_DATE"] = "2024-12-01",
            ["CIK"] = "1067983",
        };
        var args = new object[] { row, minReportDate, null };

        var resolved = (bool)method!.Invoke(null, args);

        resolved.Should().BeTrue();
        var submission = (SubmissionRow)args[2];
        submission.Should().NotBeNull();
        submission!.FormType.Should().Be("13F-HR/A");
    }
}
