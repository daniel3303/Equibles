using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceTryParseSubmissionRowBoundaryTests
{
    private static readonly MethodInfo TryParseSubmissionRowMethod =
        typeof(HoldingsImportService).GetMethod(
            "TryParseSubmissionRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // TryParseSubmissionRow (extracted in #1527) filters submissions by report
    // date with `reportDateCheck < context.MinReportDate` — strict less-than,
    // so a PERIODOFREPORT exactly equal to MinReportDate is included. A
    // single-character refactor to `<=` would compile cleanly, pass every
    // existing integration test (none feed an exact-boundary date), and
    // silently exclude every submission whose report date sits on the window
    // boundary — the holdings backfill would lose a full reporting day at
    // the leading edge of any tightened import window.
    [Fact]
    public void TryParseSubmissionRow_PeriodOfReportEqualsMinReportDate_IsIncluded()
    {
        var minReportDate = new DateOnly(2024, 9, 30);
        var row = new Dictionary<string, string>
        {
            ["SUBMISSIONTYPE"] = "13F-HR",
            ["ACCESSION_NUMBER"] = "0000950123-24-006477",
            ["PERIODOFREPORT"] = "2024-09-30",
            ["FILING_DATE"] = "2024-11-15",
            ["CIK"] = "1067983",
        };
        var args = new object[] { row, minReportDate, null };

        var resolved = (bool)TryParseSubmissionRowMethod.Invoke(null, args);

        resolved.Should().BeTrue();
        var submission = (SubmissionRow)args[2];
        submission.Should().NotBeNull();
        submission.AccessionNumber.Should().Be("0000950123-24-006477");
        submission.PeriodOfReport.Should().Be("2024-09-30");
    }
}
