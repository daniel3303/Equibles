using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceTryParseSubmissionRowNon13FRejectedTests
{
    [Fact]
    public void TryParseSubmissionRow_NonThirteenFFormType_ReturnsFalse()
    {
        // Sibling to the canonical 13F-HR boundary pins and the just-added
        // 13F-HR/A amendment pin. Closes the form-type gate's rejection
        // arm: a form type that is NEITHER "13F-HR" NOR "13F-HR/A" must
        // be rejected. "13F-NT" (Notice of inactivity — Schedule 13F-NT,
        // "no holdings reportable") is the canonical adjacent form that
        // shares the "13F-" prefix and would slip past a sloppy
        // `StartsWith("13F")` rewrite. A refactor that loosens the
        // pattern to `StartsWith("13F")` would compile, pass every
        // existing acceptance pin, and ingest 13F-NT rows as if they
        // had holdings — except they don't, polluting the import with
        // metadata-only submissions that fail downstream when the
        // INFORMATIONTABLE TSV is empty.
        var method = typeof(HoldingsImportService).GetMethod(
            "TryParseSubmissionRow",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var minReportDate = new DateOnly(2024, 1, 1);
        var row = new Dictionary<string, string>
        {
            ["SUBMISSIONTYPE"] = "13F-NT",
            ["ACCESSION_NUMBER"] = "0000950123-24-009999",
            ["PERIODOFREPORT"] = "2024-09-30",
            ["FILING_DATE"] = "2024-12-01",
            ["CIK"] = "1067983",
        };
        var args = new object[] { row, minReportDate, null };

        var resolved = (bool)method!.Invoke(null, args);

        resolved.Should().BeFalse();
        args[2].Should().BeNull();
    }
}
