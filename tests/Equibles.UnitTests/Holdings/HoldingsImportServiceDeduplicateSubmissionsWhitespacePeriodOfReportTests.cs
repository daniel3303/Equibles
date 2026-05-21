using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceDeduplicateSubmissionsWhitespacePeriodOfReportTests
{
    // Sibling pin to HoldingsImportServiceDeduplicateSubmissionsWhitespaceCikTests.
    // The Cik arm is pinned; the PeriodOfReport arm of the same
    // IsNullOrWhiteSpace filter is unpinned. SEC PERIODOFREPORT values are
    // always ISO yyyy-MM-dd dates and can never legitimately be whitespace —
    // treating a whitespace period as a real one would collapse two filings
    // with the same Cik onto a single "key" and drop one of them. A refactor
    // that loosens this check to `IsNullOrEmpty` (or removes it entirely)
    // would compile, pass every existing pin, and silently lose one filing
    // per malformed pair on every quarterly import.
    [Fact]
    public void DeduplicateSubmissions_WhitespaceOnlyPeriodOfReport_SkippedFromGrouping()
    {
        var context = new ImportContext
        {
            Submissions = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase)
            {
                ["ACC-001"] = new()
                {
                    AccessionNumber = "ACC-001",
                    Cik = "1234567",
                    PeriodOfReport = "   ",
                    FilingDate = "2024-11-15",
                    FormType = "13F-HR",
                },
                ["ACC-002"] = new()
                {
                    AccessionNumber = "ACC-002",
                    Cik = "1234567",
                    PeriodOfReport = "   ",
                    FilingDate = "2024-11-20",
                    FormType = "13F-HR",
                },
            },
        };

        HoldingsImportService.DeduplicateSubmissions(context);

        context.Submissions.Should().HaveCount(2);
    }
}
