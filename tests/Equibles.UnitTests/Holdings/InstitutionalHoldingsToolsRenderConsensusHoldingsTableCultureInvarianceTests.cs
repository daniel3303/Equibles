using System.Globalization;
using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderConsensusHoldingsTableCultureInvarianceTests
{
    private static readonly MethodInfo RenderConsensusHoldingsTableMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderConsensusHoldingsTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // RenderConsensusHoldingsTable interpolates the Combined ($M) cell
    // ({CombinedValue / 1_000_000m:N1}) with no IFormatProvider, so it formats
    // through the thread CurrentCulture. Same bug class as the already-fixed
    // RenderOverlapTable (GH-2647/#2651), RenderInstitutionSummary (GH-2637) and
    // RenderSectorAllocationTable (GH-2641) siblings. The repo convention (cf.
    // FactMarkdown threading InvariantCulture) is that the same call renders
    // byte-identically regardless of host CurrentCulture.
    [Fact(Skip = "GH-2660 — RenderConsensusHoldingsTable emits host-locale digit separators")]
    public void RenderConsensusHoldingsTable_UnderNonInvariantCulture_RendersCellsCultureInvariantly()
    {
        var holders = new List<InstitutionalHolder>
        {
            new() { Name = "ACME Capital", Cik = "0000001" },
        };
        var missing = new List<string>();
        var selected = new DateOnly(2024, 12, 31);
        var rowsWithConsensus = new List<(FundOverlapRow Row, int HeldBy)>
        {
            (
                new FundOverlapRow
                {
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    CombinedValue = 9_876_500_000L,
                },
                1
            ),
        };
        object[] args = [holders, missing, selected, rowsWithConsensus];

        var original = CultureInfo.CurrentCulture;
        string invariantOutput;
        string deDeOutput;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantOutput = (string)RenderConsensusHoldingsTableMethod.Invoke(null, args);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            deDeOutput = (string)RenderConsensusHoldingsTableMethod.Invoke(null, args);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        deDeOutput
            .Should()
            .Be(
                invariantOutput,
                "MCP markdown output is consumed by LLMs trained on en-US conventions; the Combined ($M) :N1 cell follows CurrentCulture (de-DE swaps the thousand/decimal separators), forking the response by host locale — same bug class as the RenderOverlapTable, RenderInstitutionSummary and RenderSectorAllocationTable culture-invariance siblings"
            );
    }
}
