using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceDeduplicateSubmissionsSecDateMonthBoundaryTests
{
    // Contract: DeduplicateSubmissions orders survivors by FilingDate chronologically,
    // so the genuinely later filing wins regardless of the calendar-string layout.
    //
    // The bulk SEC datasets carry FilingDate as `dd-MMM-yyyy` ("29-JAN-2025"). An
    // ordinal string sort compares the day-of-month first, so "29-JAN-2025" sorts
    // AFTER "14-FEB-2025" even though it is chronologically EARLIER. When an original
    // (29-JAN) and its later amendment (14-FEB) share (Cik, PeriodOfReport) and span a
    // month boundary, an ordinal sort keeps the original and silently drops the
    // amendment — reverting amended holdings to their pre-correction values.
    //
    // This pins the chronological comparison: the FEB amendment must win over the JAN
    // original. Sibling to the ISO-date FilingDatePrimary test (where ordinal happens
    // to coincide with chronological order) and the same-day accession-tiebreaker test.
    [Fact]
    public void DeduplicateSubmissions_SecDatesSpanningMonthBoundary_LaterFilingWins()
    {
        var context = new ImportContext
        {
            Submissions = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase)
            {
                // Original — filed earlier (29 Jan) but ordinally GREATER than 14 Feb.
                ["0001234567-25-000100"] = new()
                {
                    AccessionNumber = "0001234567-25-000100",
                    Cik = "1234567",
                    PeriodOfReport = "2024-12-31",
                    FilingDate = "29-JAN-2025",
                    FormType = "13F-HR",
                },
                // Amendment — filed later (14 Feb), the genuine correction that must win.
                ["0001234567-25-000200"] = new()
                {
                    AccessionNumber = "0001234567-25-000200",
                    Cik = "1234567",
                    PeriodOfReport = "2024-12-31",
                    FilingDate = "14-FEB-2025",
                    FormType = "13F-HR/A",
                },
            },
        };

        HoldingsImportService.DeduplicateSubmissions(context);

        context.Submissions.Should().HaveCount(1);
        context.Submissions.Should().ContainKey("0001234567-25-000200");
        context.Submissions.Should().NotContainKey("0001234567-25-000100");
    }
}
