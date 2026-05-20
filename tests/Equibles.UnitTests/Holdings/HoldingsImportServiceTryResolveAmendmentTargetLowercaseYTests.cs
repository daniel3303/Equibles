using System.Reflection;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceTryResolveAmendmentTargetLowercaseYTests
{
    private static readonly MethodInfo TryResolveAmendmentTargetMethod =
        typeof(HoldingsImportService).GetMethod(
            "TryResolveAmendmentTarget",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // TryResolveAmendmentTarget (extracted in #1490) gates the IsAmendment
    // field with `string.Equals(coverPage.IsAmendment, "Y",
    // StringComparison.OrdinalIgnoreCase)`. The integration tests at the
    // HoldingsImportService level all feed uppercase "Y" (matching SEC's
    // standard convention) — the lowercase-"y" case-insensitive contract is
    // unexercised. A regression to `StringComparison.Ordinal` (single-keyword
    // deletion) would silently drop every defensively-lower-cased amendment
    // from the reconciliation pass; the original 13F would remain in place and
    // the amendment's reduced position would never be applied.
    [Fact]
    public void TryResolveAmendmentTarget_LowercaseYAmendmentFlag_ResolvesSuccessfully()
    {
        var accession = "0000950123-24-006477";
        var cik = "1067983";
        var holderGuid = Guid.NewGuid();
        var submission = new SubmissionRow
        {
            AccessionNumber = accession,
            Cik = cik,
            PeriodOfReport = "2024-09-30",
        };
        var context = new ImportContext
        {
            CoverPages = new Dictionary<string, CoverPageRow>
            {
                [accession] = new() { AccessionNumber = accession, IsAmendment = "y" },
            },
            CikToHolderId = new Dictionary<string, Guid> { [cik] = holderGuid },
        };
        var args = new object[] { accession, submission, context, null, null };

        var resolved = (bool)TryResolveAmendmentTargetMethod.Invoke(null, args);

        resolved.Should().BeTrue();
        ((Guid)args[3]).Should().Be(holderGuid);
        ((DateOnly)args[4]).Should().Be(new DateOnly(2024, 9, 30));
    }
}
