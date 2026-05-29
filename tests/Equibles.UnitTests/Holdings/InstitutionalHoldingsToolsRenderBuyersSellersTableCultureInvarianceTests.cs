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

    // RenderBuyersSellersTable's AppendMoverSection renders the "Prior → New
    // Shares" column with the culture-implicit :N0 specifier on PreviousShares
    // / CurrentShares, which resolves through the thread CurrentCulture. The
    // rest of the row (FormatSignedShares / FormatSignedMillions) already
    // threads InvariantCulture explicitly. Same bug class as the already-fixed
    // RenderTopHoldersTable (GH-2628), RenderInstitutionSummary (GH-2637),
    // RenderSectorAllocationTable (GH-2641), and RenderOverlapTable (GH-2647)
    // siblings: de-DE swaps the thousand separator (1,234,567 → 1.234.567),
    // forking the LLM-consumed markdown by host locale. The contract is that
    // the same call renders byte-identically regardless of host CurrentCulture.
    [Fact]
    public void RenderBuyersSellersTable_UnderNonInvariantCulture_RendersCellsCultureInvariantly()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var topBuyers = new List<(
            string Name,
            long CurrentShares,
            long PreviousShares,
            long DeltaShares,
            long DeltaValue
        )>
        {
            ("ACME Capital", 7_654_321L, 1_234_567L, 6_419_754L, 1_234_600_000L),
        };
        var topSellers =
            new List<(
                string Name,
                long CurrentShares,
                long PreviousShares,
                long DeltaShares,
                long DeltaValue
            )>();
        object[] args =
        [
            stock,
            "AAPL",
            new DateOnly(2024, 12, 31),
            (DateOnly?)new DateOnly(2024, 9, 30),
            topBuyers,
            topSellers,
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
                "MCP markdown output is consumed by LLMs trained on en-US conventions; the bare :N0 cells in the \"Prior → New Shares\" column follow CurrentCulture (de-DE → 1.234.567 → 7.654.321), forking the response by host locale — same bug class as the RenderTopHoldersTable culture-invariance sibling"
            );
    }
}
