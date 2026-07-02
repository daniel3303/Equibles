using System.Globalization;
using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderBuyersSellersTableCultureInvarianceTests
{
    private static readonly MethodInfo RenderBuyersSellersTableMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderBuyersSellersTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // RenderBuyersSellersTable builds the "Prior → New Shares" column with the
    // culture-implicit :N0 specifier (m.PreviousShares / m.CurrentShares), which
    // resolves through the thread CurrentCulture. Same bug class as the already
    // -fixed RenderTopHoldersTable / RenderInstitutionSummary / RenderOverlapTable
    // / RenderSectorAllocationTable siblings: de-DE swaps the thousand separator
    // (1,234,567 → 1.234.567), forking the LLM-consumed markdown by host locale.
    // The contract (this file's own helpers thread InvariantCulture explicitly,
    // commenting "MCP markdown must not fork the separators by host locale") is
    // that the same call renders byte-identically regardless of host CurrentCulture.
    [Fact]
    public void RenderBuyersSellersTable_UnderNonInvariantCulture_RendersCellsCultureInvariantly()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var buyers = new List<(
            string Name,
            long CurrentShares,
            long PreviousShares,
            long DeltaShares,
            long DeltaValue
        )>
        {
            ("ACME Capital", 7_654_321L, 1_234_567L, 6_419_754L, 1_234_567_890L),
        };
        var sellers = new List<(
            string Name,
            long CurrentShares,
            long PreviousShares,
            long DeltaShares,
            long DeltaValue
        )>
        {
            ("Globex Advisors", 2_345_678L, 9_876_543L, -7_530_865L, -2_345_678_901L),
        };
        object[] args =
        [
            stock,
            "AAPL",
            new DateOnly(2024, 12, 31),
            (DateOnly?)new DateOnly(2024, 9, 30),
            buyers,
            sellers,
            null,
        ];

        var original = CultureInfo.CurrentCulture;
        string invariantOutput;
        string deDeOutput;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantOutput = (string)RenderBuyersSellersTableMethod.Invoke(null, args);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            deDeOutput = (string)RenderBuyersSellersTableMethod.Invoke(null, args);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        deDeOutput
            .Should()
            .Be(
                invariantOutput,
                "MCP markdown output is consumed by LLMs trained on en-US conventions; the :N0 cells in the 'Prior → New Shares' column without an explicit IFormatProvider follow CurrentCulture (de-DE → 1.234.567), forking the response by host locale — same bug class as the RenderTopHoldersTable culture-invariance sibling"
            );
    }
}
