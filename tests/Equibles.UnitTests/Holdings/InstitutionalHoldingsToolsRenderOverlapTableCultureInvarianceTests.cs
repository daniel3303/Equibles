using System.Globalization;
using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderOverlapTableCultureInvarianceTests
{
    private static readonly MethodInfo RenderOverlapTableMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderOverlapTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // RenderOverlapTable builds the summary cells (union/shared positions :N0,
    // Jaccard / $-weighted overlap :F1) and each row's A/B shares (.ToString("N0")
    // with no IFormatProvider), A/B % (.ToString("F1")) and Combined $M (:N1) with
    // culture-implicit formatting that resolves through the thread CurrentCulture.
    // Same bug class as the already-fixed RenderInstitutionPortfolio (GH-2597),
    // RenderTopHoldersTable (GH-2628), RenderInstitutionSummary (GH-2637) and
    // RenderSectorAllocationTable (GH-2641) siblings in this class: de-DE swaps the
    // thousand/decimal separators, forking the LLM-consumed MCP output by host
    // locale. The contract (repo convention; cf. FactMarkdown threading
    // InvariantCulture) is that the same call renders byte-identically regardless
    // of host CurrentCulture.
    [Fact]
    public void RenderOverlapTable_UnderNonInvariantCulture_RendersCellsCultureInvariantly()
    {
        var holder1 = new InstitutionalHolder { Name = "ACME Capital" };
        var holder2 = new InstitutionalHolder { Name = "Globex Advisors" };
        var selected = new DateOnly(2024, 12, 31);
        var overlap = new FundOverlapResult
        {
            ReportDate = selected,
            UnionPositionCount = 1_234,
            IntersectionPositionCount = 567,
            JaccardSimilarityPercent = 45.67,
            DollarWeightedOverlapPercent = 12.34,
            Rows =
            {
                new FundOverlapRow
                {
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    CombinedValue = 9_876_543_210L,
                    Slices =
                    {
                        new FundOverlapRowSlice
                        {
                            Shares = 12_345L,
                            Value = 5_000_000_000L,
                            PercentOfPortfolio = 23.45,
                        },
                        new FundOverlapRowSlice
                        {
                            Shares = 67_890L,
                            Value = 4_000_000_000L,
                            PercentOfPortfolio = 34.56,
                        },
                    },
                },
            },
        };
        object[] args = [holder1, holder2, selected, overlap, 30, null];

        var original = CultureInfo.CurrentCulture;
        string invariantOutput;
        string deDeOutput;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantOutput = (string)RenderOverlapTableMethod.Invoke(null, args);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            deDeOutput = (string)RenderOverlapTableMethod.Invoke(null, args);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        deDeOutput
            .Should()
            .Be(
                invariantOutput,
                "MCP markdown output is consumed by LLMs trained on en-US conventions; the :N0 / :F1 / :N1 and provider-less .ToString(\"N0\")/.ToString(\"F1\") cells follow CurrentCulture (de-DE swaps the thousand/decimal separators), forking the response by host locale — same bug class as the RenderInstitutionPortfolio, RenderTopHoldersTable, RenderInstitutionSummary and RenderSectorAllocationTable culture-invariance siblings"
            );
    }
}
