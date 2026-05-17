using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.IntegrationTests.Holdings;

public class HoldingsImportServiceDeduplicateTiebreakerTests
{
    [Fact]
    public void DeduplicateSubmissions_SameDayOriginalAndAmendment_KeepsAmendmentDeterministically()
    {
        // Contract: DeduplicateSubmissions collapses every submission sharing
        // (Cik, PeriodOfReport) to the single latest filing. An amendment
        // supersedes the original it amends, so when the real-time path lifts
        // FilingDate from the day-granular daily index an original and its
        // 13F-HR/A can tie on the exact same date. The survivor must still be
        // deterministic AND must be the amendment — if the original wins, the
        // pipeline upserts stale pre-amendment holdings and silently reverts
        // the correction. SEC assigns accession numbers monotonically, so the
        // lexicographically greater accession is the later (amendment) filing.
        var context = new ImportContext
        {
            Submissions = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase)
            {
                ["0001234567-26-000100"] = new()
                {
                    AccessionNumber = "0001234567-26-000100",
                    Cik = "1234567",
                    PeriodOfReport = "2026-03-31",
                    FilingDate = "2026-05-15",
                    FormType = "13F-HR",
                },
                ["0001234567-26-000200"] = new()
                {
                    AccessionNumber = "0001234567-26-000200",
                    Cik = "1234567",
                    PeriodOfReport = "2026-03-31",
                    FilingDate = "2026-05-15",
                    FormType = "13F-HR/A",
                },
            },
        };

        HoldingsImportService.DeduplicateSubmissions(context);

        context.Submissions.Should().HaveCount(1);
        context
            .Submissions.Should()
            .ContainKey("0001234567-26-000200")
            .WhoseValue.FormType.Should()
            .Be("13F-HR/A");
        context.Submissions.Should().NotContainKey("0001234567-26-000100");
    }
}
