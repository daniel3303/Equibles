using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientFilterFilingsDocumentTypeTests
{
    [Fact]
    public void FilterFilings_DocumentTypeFilterSet_DropsFilingsWithDifferentForm()
    {
        // Sibling to FromDateBoundary / ToDateBoundary pins. Existing pins
        // exercise the date arms of the AND chain — the documentType arm
        // is unpinned. A refactor that drops the
        //   `!documentType.HasValue || f.Form == documentType.Value.GetFormName()`
        // term (or flips the equality to `!=`) would silently let every
        // form through regardless of the caller's filter. The MCP tool
        // exposes documentType as the primary way a user/LLM scopes a
        // company's filings to e.g. 10-Ks only; without this guard, asking
        // for 10-K returns 8-Ks and S-1s mixed in. Pin: filings list of
        // [10-K, 8-K, 10-Q]; filter=TenK; expect only the 10-K row.
        var filter = typeof(SecEdgarClient).GetMethod(
            "FilterFilings",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var tenK = new FilingData
        {
            AccessionNumber = "A-10K",
            FilingDate = new DateOnly(2024, 11, 1),
            Form = "10-K",
        };
        var eightK = new FilingData
        {
            AccessionNumber = "A-8K",
            FilingDate = new DateOnly(2024, 11, 2),
            Form = "8-K",
        };
        var tenQ = new FilingData
        {
            AccessionNumber = "A-10Q",
            FilingDate = new DateOnly(2024, 11, 3),
            Form = "10-Q",
        };
        var filings = new List<FilingData> { tenK, eightK, tenQ };

        var result =
            (List<FilingData>)
                filter!.Invoke(
                    null,
                    [filings, (DocumentTypeFilter?)DocumentTypeFilter.TenK, null, null]
                );

        result.Should().ContainSingle();
        result[0].AccessionNumber.Should().Be("A-10K");
    }
}
