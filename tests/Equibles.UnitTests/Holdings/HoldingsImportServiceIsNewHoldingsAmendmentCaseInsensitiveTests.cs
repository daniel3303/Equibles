using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceIsNewHoldingsAmendmentCaseInsensitiveTests
{
    private static readonly MethodInfo IsNewHoldingsAmendmentMethod =
        typeof(HoldingsImportService).GetMethod(
            "IsNewHoldingsAmendment",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // Contract: amendments whose cover-page AmendmentType is "NEW HOLDINGS"
    // merge into existing positions without deleting them; "RESTATEMENT" and
    // legacy/blank values delete-and-replace. The match uses
    // StringComparison.OrdinalIgnoreCase — SEC EDGAR has emitted the field in
    // mixed case across submission generators ("new holdings", "NEW HOLDINGS",
    // "New Holdings"). A refactor to strict ordinal comparison would route the
    // lowercase variant through the RESTATEMENT branch and wipe out a manager's
    // entire prior 13F snapshot on every additive amendment — quiet, large data
    // loss the existing happy-path test wouldn't catch (it uses the canonical
    // upper-case form).
    [Fact]
    public void IsNewHoldingsAmendment_LowercaseAmendmentType_ReturnsTrue()
    {
        var accession = "0000950123-24-006477";
        var context = new ImportContext
        {
            CoverPages = new Dictionary<string, CoverPageRow>
            {
                [accession] = new()
                {
                    AccessionNumber = accession,
                    IsAmendment = "Y",
                    AmendmentType = "new holdings",
                },
            },
        };

        var result = (bool)IsNewHoldingsAmendmentMethod.Invoke(null, [accession, context]);

        result
            .Should()
            .BeTrue(
                "AmendmentType comparison must be case-insensitive so additive amendments are never demoted to RESTATEMENT"
            );
    }
}
