using System.Globalization;
using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderMostHeldStocksTableCultureInvarianceTests
{
    private static readonly MethodInfo RenderMostHeldStocksTableMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderMostHeldStocksTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // RenderMostHeldStocksTable interpolates the universe filer count (:N0), each
    // row's filer count / delta (:N0), $ value ($M, :N1), delta $ value
    // (:+#,##0.0;…) and % of universe (:F1) with no IFormatProvider, so they
    // resolve through the thread CurrentCulture. Same bug class as the already-
    // fixed RenderOverlapTable (GH-2647/#2651), RenderInstitutionSummary
    // (GH-2637) and RenderSectorAllocationTable (GH-2641) siblings. The repo
    // convention (cf. FactMarkdown threading InvariantCulture) is that the same
    // call renders byte-identically regardless of host CurrentCulture.
    [Fact(Skip = "GH-2656 — RenderMostHeldStocksTable emits host-locale digit separators")]
    public void RenderMostHeldStocksTable_UnderNonInvariantCulture_RendersCellsCultureInvariantly()
    {
        var targetDate = new DateOnly(2024, 12, 31);
        var previousDate = new DateOnly(2024, 9, 30);
        var rows = new List<MarketWideStockActivity>
        {
            new()
            {
                CommonStockId = Guid.Empty,
                CurrentFilerCount = 617,
                PreviousFilerCount = 500,
                CurrentValue = 9_876_500_000L,
                PreviousValue = 1_000_000_000L,
            },
        };
        var stocks = new Dictionary<Guid, CommonStock>();
        object[] args = [targetDate, previousDate, "filers", 1_234, rows, stocks];

        var original = CultureInfo.CurrentCulture;
        string invariantOutput;
        string deDeOutput;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantOutput = (string)RenderMostHeldStocksTableMethod.Invoke(null, args);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            deDeOutput = (string)RenderMostHeldStocksTableMethod.Invoke(null, args);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        deDeOutput
            .Should()
            .Be(
                invariantOutput,
                "MCP markdown output is consumed by LLMs trained on en-US conventions; the :N0 / :N1 / :F1 / signed delta cells follow CurrentCulture (de-DE swaps the thousand/decimal separators), forking the response by host locale — same bug class as the RenderOverlapTable, RenderInstitutionSummary and RenderSectorAllocationTable culture-invariance siblings"
            );
    }
}
