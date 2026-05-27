using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceTryParseSubmissionRowCikLeadingZeroTests
{
    [Fact]
    public void TryParseSubmissionRow_CikWithLeadingZeros_StripsZerosForCanonicalForm()
    {
        // TryParseSubmissionRow's `Cik = GetValue(row, "CIK")?.TrimStart('0')`
        // (HoldingsImportService.cs:144) normalizes the SEC SUBMISSION.tsv
        // CIK field — which arrives zero-padded to 10 digits — into the
        // canonical numeric form the rest of the pipeline uses (CommonStock
        // stores CIKs unpadded; subsidiary-CIK joins are string-compared).
        // A refactor that drops the TrimStart (or uses Trim instead of
        // TrimStart) would silently double-store the same logical CIK as
        // both "320193" and "0000320193" — preventing every SEC archive
        // submission from joining its incumbent CommonStock row, so every
        // 13F-HR holding ingested from the bulk archive feed would be
        // dropped at the CIK-to-stock map step. Pin the TrimStart semantic
        // explicitly on a real-shaped zero-padded SEC CIK.
        var method = typeof(HoldingsImportService).GetMethod(
            "TryParseSubmissionRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var minReportDate = new DateOnly(2024, 1, 1);
        var row = new Dictionary<string, string>
        {
            ["SUBMISSIONTYPE"] = "13F-HR",
            ["ACCESSION_NUMBER"] = "0000950123-24-006477",
            ["PERIODOFREPORT"] = "2024-09-30",
            ["FILING_DATE"] = "2024-11-15",
            ["CIK"] = "0000320193",
        };
        var args = new object[] { row, minReportDate, null };

        var resolved = (bool)method!.Invoke(null, args);

        resolved.Should().BeTrue();
        var submission = (SubmissionRow)args[2];
        submission!.Cik.Should().Be("320193");
    }
}
