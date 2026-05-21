using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceTryParseSubmissionRowCutoffBoundaryTests
{
    [Fact]
    public void TryParseSubmissionRow_PeriodOfReportEqualsMinReportDate_KeepsTheRow()
    {
        // TryParseSubmissionRow (#1527) gates which SUBMISSION.tsv rows the
        // bulk 13F importer accepts. The cutoff arm reads
        //   if (TryParseDateOnly(periodOfReport, out var reportDateCheck)
        //       && reportDateCheck < minReportDate)
        //       return false;
        // The `<` (strict-less-than) is the load-bearing detail: rows whose
        // PERIODOFREPORT exactly equals `minReportDate` are KEPT, not
        // filtered. `minReportDate` is the operator's configured "earliest
        // quarter to import" — it is inclusive by intent, since callers
        // pass the first date of the desired window (e.g. 2024-12-31 means
        // "include Q4 2024 and forward").
        //
        // The risk this catches: a refactor that "tightens" the comparison
        // to `<=` (perhaps because "rows BEFORE the cutoff are rejected,
        // so the cutoff itself is the boundary of what's rejected" reads
        // naturally) would compile, pass any test that uses dates
        // strictly before or strictly after the cutoff, and silently drop
        // every 13F filing for the exact cutoff quarter on every
        // configured-window import. The next quarter's screener and
        // backtest views then show a missing-quarter gap that's
        // attributed to upstream data rather than the import filter.
        //
        // Pin: a row whose PERIODOFREPORT equals minReportDate must
        // produce a SubmissionRow (TryParse returns true). Use a string
        // value formatted exactly as SEC's SUBMISSION.tsv emits.
        var method = typeof(HoldingsImportService).GetMethod(
            "TryParseSubmissionRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SUBMISSIONTYPE"] = "13F-HR",
            ["ACCESSION_NUMBER"] = "0001067983-26-000300",
            ["PERIODOFREPORT"] = "2024-12-31",
            ["FILING_DATE"] = "2025-02-14",
            ["CIK"] = "1067983",
        };
        var minReportDate = new DateOnly(2024, 12, 31);

        object[] args = [row, minReportDate, null];
        var success = (bool)method.Invoke(null, args);
        var submission = (SubmissionRow)args[2];

        success.Should().BeTrue();
        submission.Should().NotBeNull();
        submission.PeriodOfReport.Should().Be("2024-12-31");
        submission.AccessionNumber.Should().Be("0001067983-26-000300");
    }
}
