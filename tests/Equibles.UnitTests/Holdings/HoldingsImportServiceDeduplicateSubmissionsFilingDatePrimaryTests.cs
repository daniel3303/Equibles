using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceDeduplicateSubmissionsFilingDatePrimaryTests
{
    // Contract: DeduplicateSubmissions orders survivors by FilingDate
    // DESCENDING first, then by AccessionNumber descending as the tiebreaker.
    // FilingDate is the primary key — when two filings share (Cik,
    // PeriodOfReport) but differ on FilingDate, the later FilingDate wins
    // regardless of which side carries the greater AccessionNumber.
    //
    // Sibling pin to the same-day tiebreaker test in
    // HoldingsImportServiceDeduplicateTiebreakerTests: that test fixes
    // FilingDate equal across both rows so the accession arm decides;
    // this test fixes the FilingDates DIFFERENT and makes the OLDER
    // FilingDate carry the GREATER AccessionNumber. A refactor that
    // swapped the order — `.OrderByDescending(s => s.AccessionNumber)
    // .ThenByDescending(s => s.FilingDate)` — would compile, pass the
    // same-day sibling (accession tiebreaker still decides), and silently
    // pick the wrong winner on every cross-day amendment: the original
    // (greater accession because filed first by a different agent, or
    // an internal numbering artefact) would beat its later-filed
    // correction, reverting amended holdings to pre-correction values
    // on every quarterly import.
    [Fact]
    public void DeduplicateSubmissions_LaterFilingDateWithSmallerAccession_WinsOverEarlierWithGreaterAccession()
    {
        var context = new ImportContext
        {
            Submissions = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase)
            {
                ["0001234567-26-000200"] = new()
                {
                    AccessionNumber = "0001234567-26-000200",
                    Cik = "1234567",
                    PeriodOfReport = "2026-03-31",
                    FilingDate = "2026-05-15",
                    FormType = "13F-HR",
                },
                ["0001234567-26-000100"] = new()
                {
                    AccessionNumber = "0001234567-26-000100",
                    Cik = "1234567",
                    PeriodOfReport = "2026-03-31",
                    FilingDate = "2026-05-20",
                    FormType = "13F-HR/A",
                },
            },
        };

        HoldingsImportService.DeduplicateSubmissions(context);

        context.Submissions.Should().HaveCount(1);
        context.Submissions.Should().ContainKey("0001234567-26-000100");
        context.Submissions.Should().NotContainKey("0001234567-26-000200");
    }
}
