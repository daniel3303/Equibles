using System.Globalization;
using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderSectorAllocationTableCultureInvarianceTests
{
    private static readonly MethodInfo RenderSectorAllocationTableMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderSectorAllocationTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // RenderSectorAllocationTable builds each row's # Positions (:N0), Value $M
    // (:N1) and % of Portfolio (:F1) with culture-implicit format specifiers that
    // resolve through the thread CurrentCulture. Same bug class as the already-fixed
    // RenderInstitutionPortfolio (GH-2597), RenderTopHoldersTable (GH-2628) and
    // RenderInstitutionSummary (GH-2637) siblings in this class: de-DE swaps the
    // thousand/decimal separators (1,234 → 1.234, 12,345.7 → 12.345,7, 42.3 → 42,3),
    // forking the LLM-consumed MCP output by host locale. The contract (repo
    // convention; cf. FactMarkdown threading InvariantCulture) is that the same call
    // renders byte-identically regardless of host CurrentCulture.
    [Fact]
    public void RenderSectorAllocationTable_UnderNonInvariantCulture_RendersRowCellsCultureInvariantly()
    {
        var holder = new InstitutionalHolder { Name = "ACME Capital" };
        var targetDate = new DateOnly(2024, 12, 31);
        var slices = new List<IndustryAllocationSlice>
        {
            new()
            {
                IndustryName = "Technology",
                PositionCount = 1_234,
                TotalValue = 12_345_678_900L,
                PercentOfPortfolio = 42.34,
            },
        };
        object[] args = [holder, targetDate, slices, "Industry", null];

        var original = CultureInfo.CurrentCulture;
        string invariantOutput;
        string deDeOutput;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantOutput = (string)RenderSectorAllocationTableMethod.Invoke(null, args);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            deDeOutput = (string)RenderSectorAllocationTableMethod.Invoke(null, args);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        deDeOutput
            .Should()
            .Be(
                invariantOutput,
                "MCP markdown output is consumed by LLMs trained on en-US conventions; the :N0 / :N1 / :F1 row cells without an explicit IFormatProvider follow CurrentCulture (de-DE → 1.234 / 12.345,7 / 42,3), forking the response by host locale — same bug class as the RenderInstitutionPortfolio, RenderTopHoldersTable and RenderInstitutionSummary culture-invariance siblings"
            );
    }
}
