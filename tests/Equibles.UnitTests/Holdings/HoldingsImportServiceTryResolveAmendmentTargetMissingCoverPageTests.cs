using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceTryResolveAmendmentTargetMissingCoverPageTests
{
    private static readonly MethodInfo TryResolveAmendmentTargetMethod =
        typeof(HoldingsImportService).GetMethod(
            "TryResolveAmendmentTarget",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Fact]
    public void TryResolveAmendmentTarget_AccessionMissingFromCoverPages_ReturnsFalseWithoutDereferencingNull()
    {
        // Sibling to NullCik / UnknownCik / LowercaseY pins. The existing
        // pins all assume the accession IS in context.CoverPages. The first
        // guard in TryResolveAmendmentTarget (HoldingsImportService.cs:553-554)
        // is `if (!context.CoverPages.TryGetValue(accession, out var
        // coverPage)) return false;` — the load-bearing null-protection
        // for the NEXT line, which would NRE on `coverPage.IsAmendment`
        // for a null cover page. A refactor that drops the guard (e.g.
        // `var coverPage = context.CoverPages[accession];` using the
        // indexer under "the accession always has a cover page") would
        // KeyNotFoundException — crashing the whole amendment-resolution
        // pass on the first orphan accession (filings whose cover page
        // row was rejected upstream still appear in the submission map).
        // Pin the silent-false contract for a missing cover page.
        var accession = "0000950123-24-006477";
        var submission = new SubmissionRow
        {
            AccessionNumber = accession,
            Cik = "1067983",
            PeriodOfReport = "2024-09-30",
        };
        var context = new ImportContext
        {
            CoverPages = new Dictionary<string, CoverPageRow>(), // accession NOT here
            CikToHolderId = new Dictionary<string, Guid> { ["1067983"] = Guid.NewGuid() },
        };
        var args = new object[] { accession, submission, context, null, null };

        var resolved = (bool)TryResolveAmendmentTargetMethod!.Invoke(null, args);

        resolved.Should().BeFalse();
    }
}
