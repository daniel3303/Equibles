using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceIsNewHoldingsAmendmentMissingAccessionTests
{
    [Fact]
    public void IsNewHoldingsAmendment_AccessionNotInCoverPages_ReturnsFalseWithoutThrowing()
    {
        // Sibling to the CaseInsensitive pin. The existing pin defends the
        // SECOND arm of the `&&` chain (AmendmentType equality semantic).
        // This pins the FIRST arm — `CoverPages.TryGetValue(accession, ...)`
        // — on the miss path. A refactor that drops the TryGetValue and
        // uses the indexer (`var coverPage = context.CoverPages[accession]`
        // under "the accession always has a cover page") would throw
        // KeyNotFoundException on every orphan accession (filings whose
        // cover page row was rejected upstream still appear in the
        // submission map). The downstream amendment-classification pass
        // would crash instead of safely falling through to the
        // delete-and-replace (RESTATEMENT) path. Pin: missing accession
        // returns false, no throw.
        var method = typeof(HoldingsImportService).GetMethod(
            "IsNewHoldingsAmendment",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var accession = "0000950123-24-999999";
        var context = new ImportContext { CoverPages = new Dictionary<string, CoverPageRow>() };

        var result = (bool)method!.Invoke(null, [accession, context]);

        result.Should().BeFalse();
    }
}
