using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceTryResolveAmendmentTargetUnknownCikTests
{
    private static readonly MethodInfo TryResolveAmendmentTargetMethod =
        typeof(HoldingsImportService).GetMethod(
            "TryResolveAmendmentTarget",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // The sibling NullCik pin covers `Cik == null`. This pin covers the
    // ADJACENT arm — a well-formed Cik that simply isn't in
    // `context.CikToHolderId` (an amendment for a filer outside this import
    // batch's universe, or one whose first-seen filing failed earlier in the
    // pipeline). The body uses `TryGetValue`; a regression that "simplified"
    // it to `_dict[submission.Cik]` would throw KeyNotFoundException on the
    // first orphan amendment and abort the whole amendment-reconciliation
    // pass — the strict-Try*-never-throws contract demands a clean `false`.
    [Fact]
    public void TryResolveAmendmentTarget_CikNotInHolderMap_ReturnsFalseWithoutThrowing()
    {
        var accession = "0000950123-24-006478";
        var submission = new SubmissionRow
        {
            AccessionNumber = accession,
            Cik = "0009999999",
            PeriodOfReport = "2024-09-30",
        };
        var context = new ImportContext
        {
            CoverPages = new Dictionary<string, CoverPageRow>
            {
                [accession] = new() { AccessionNumber = accession, IsAmendment = "Y" },
            },
            CikToHolderId = new Dictionary<string, Guid>
            {
                // A DIFFERENT CIK is mapped — submission.Cik is intentionally absent.
                ["0001067983"] = Guid.NewGuid(),
            },
        };
        var args = new object[] { accession, submission, context, null, null };

        bool resolved = false;
        var act = () => resolved = (bool)TryResolveAmendmentTargetMethod.Invoke(null, args);

        act.Should().NotThrow();
        resolved.Should().BeFalse();
    }
}
