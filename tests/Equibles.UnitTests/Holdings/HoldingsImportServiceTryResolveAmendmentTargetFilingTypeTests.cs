using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceTryResolveAmendmentTargetFilingTypeTests
{
    private static readonly MethodInfo TryResolveAmendmentTargetMethod =
        typeof(HoldingsImportService).GetMethod(
            "TryResolveAmendmentTarget",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // The resolved FilingType is what scopes the restatement delete: a Schedule
    // 13G/A amendment must resolve to Schedule13G so HandleAmendments only deletes
    // 13G rows, never the 13F-HR portfolio sharing the same (holder, quarter).
    // A 13F-HR/A resolves to Form13F, and an unrecognised form falls back to
    // Form13F (matching the historical 13F-only delete).
    [Theory]
    [InlineData("13F-HR/A", FilingType.Form13F)]
    [InlineData("SCHEDULE 13G/A", FilingType.Schedule13G)]
    [InlineData("SCHEDULE 13D/A", FilingType.Schedule13D)]
    [InlineData("SOMETHING-ELSE", FilingType.Form13F)]
    public void TryResolveAmendmentTarget_ResolvesFilingTypeFromSubmissionForm(
        string formType,
        FilingType expected
    )
    {
        var accession = "0000950123-24-006477";
        var cik = "1067983";
        var submission = new SubmissionRow
        {
            AccessionNumber = accession,
            Cik = cik,
            PeriodOfReport = "2024-09-30",
            FormType = formType,
        };
        var context = new ImportContext
        {
            CoverPages = new Dictionary<string, CoverPageRow>
            {
                [accession] = new() { AccessionNumber = accession, IsAmendment = "Y" },
            },
            CikToHolderId = new Dictionary<string, Guid> { [cik] = Guid.NewGuid() },
        };
        // Out params: holderId, reportDate, filingType.
        var args = new object[] { accession, submission, context, null, null, null };

        var resolved = (bool)TryResolveAmendmentTargetMethod.Invoke(null, args);

        resolved.Should().BeTrue();
        args[5].Should().Be(expected);
    }
}
