using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceTryResolveAmendmentTargetMalformedPeriodOfReportTests
{
    private static readonly MethodInfo TryResolveAmendmentTargetMethod =
        typeof(HoldingsImportService).GetMethod(
            "TryResolveAmendmentTarget",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Fact]
    public void TryResolveAmendmentTarget_PeriodOfReportUnparseable_ReturnsFalseAndLeavesReportDateAtDefault()
    {
        // Sibling to MissingCoverPage / NullCik / UnknownCik / LowercaseY pins.
        // Those cover arms 1, 3, 4 and the success path; this pins arm 5 —
        // the final guard at HoldingsImportService.cs:603-604:
        //
        //   if (!TryParseDateOnly(submission.PeriodOfReport, out reportDate))
        //       return false;
        //
        // Setup is the success path with ONE field corrupted (garbage
        // PeriodOfReport that neither HoldingsParsingHelper.TryParseDateOnly
        // branch can parse — not ISO yyyy-MM-dd, not SEC dd-MMM-yyyy).
        // Every other arm passes, so a regression that drops or weakens
        // this guard would surface here and nowhere else.
        //
        // The risk this catches: a refactor that elides the guard (e.g.
        // ignoring the bool return from TryParseDateOnly under "well-formed
        // 13F filings always have a parseable PeriodOfReport") would propagate
        // a sentinel `default(DateOnly)` (0001-01-01) downstream through
        // HandleAmendments → entriesByKey lookups against BuildHoldingKey
        // (which serializes reportDate into the dedup key). Every malformed
        // amendment would silently collide with every other malformed
        // amendment on the same (holderId, stockId, shareType, optionType,
        // filingType) tuple — fanning out into duplicate-row corruption
        // exactly like the #2594 culture bug, but triggered by upstream
        // data instead of host locale.
        var accession = "0000950123-24-006477";
        var holderId = Guid.NewGuid();
        var submission = new SubmissionRow
        {
            AccessionNumber = accession,
            Cik = "1067983",
            PeriodOfReport = "not-a-date",
        };
        var context = new ImportContext
        {
            CoverPages = new Dictionary<string, CoverPageRow>
            {
                [accession] = new CoverPageRow { AccessionNumber = accession, IsAmendment = "Y" },
            },
            CikToHolderId = new Dictionary<string, Guid> { ["1067983"] = holderId },
        };
        // Out params: holderId, reportDate, filingType.
        var args = new object[] { accession, submission, context, null, null, null };

        var resolved = (bool)TryResolveAmendmentTargetMethod!.Invoke(null, args);

        resolved.Should().BeFalse();
        ((DateOnly)args[4]).Should().Be(default(DateOnly));
    }
}
