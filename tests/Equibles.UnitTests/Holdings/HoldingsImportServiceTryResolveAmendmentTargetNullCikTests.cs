using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceTryResolveAmendmentTargetNullCikTests
{
    private static readonly MethodInfo TryResolveAmendmentTargetMethod =
        typeof(HoldingsImportService).GetMethod(
            "TryResolveAmendmentTarget",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // TryParseSubmissionRow does not validate Cik (per the GH-1583 discussion),
    // and HoldingsParsingHelper.GetValue returns null when a column is missing,
    // so a SUBMISSION.tsv row without a "CIK" column produces a SubmissionRow
    // whose Cik is null. Per the strict-Try*-never-throws-on-bad-input contract,
    // TryResolveAmendmentTarget must return false on a null Cik (the amendment
    // can't be reconciled), not throw. The body reaches
    // `context.CikToHolderId.TryGetValue(submission.Cik, out holderId)`; a
    // Dictionary<string, Guid> throws ArgumentNullException on a null key, so
    // an unguarded call aborts the entire amendment-reconciliation pass on the
    // first malformed row instead of skipping it.
    [Fact(
        Skip = "GH-1628 — TryResolveAmendmentTarget throws ArgumentNullException on null Cik instead of returning false"
    )]
    public void TryResolveAmendmentTarget_NullCik_ReturnsFalseWithoutThrowing()
    {
        var accession = "0000950123-24-006477";
        var submission = new SubmissionRow
        {
            AccessionNumber = accession,
            Cik = null,
            PeriodOfReport = "2024-09-30",
        };
        var context = new ImportContext
        {
            CoverPages = new Dictionary<string, CoverPageRow>
            {
                [accession] = new() { AccessionNumber = accession, IsAmendment = "Y" },
            },
            CikToHolderId = new Dictionary<string, Guid>(),
        };
        var args = new object[] { accession, submission, context, null, null };

        // MethodInfo.Invoke wraps a thrown inner exception in
        // TargetInvocationException, so `.NotThrow()` catches the NRE case.
        bool resolved = false;
        var act = () => resolved = (bool)TryResolveAmendmentTargetMethod.Invoke(null, args);

        act.Should().NotThrow();
        resolved.Should().BeFalse();
    }
}
