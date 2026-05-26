using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderOverlapTableEmptyRowsTests
{
    // RenderOverlapTable renders the GetFundOverlap LLM-facing report. When
    // FundOverlapCalculator returns no rows (both funds had no positions on
    // the selected date, or both reported only options), the renderer must
    // emit an explicit operator-readable diagnostic — not a bare markdown
    // table-header skeleton with no rows. The empty diagnostic is what tells
    // the LLM consumer the absence-of-rows is meaningful rather than missing
    // data. A refactor that dropped the early return would leave just
    // "| # | Ticker | Company | ... | Combined ($M) |" with no body, leaving
    // the consumer to guess whether the call succeeded or returned partial.
    [Fact]
    public void RenderOverlapTable_OverlapHasNoRows_EmitsExplicitNoPositionsDiagnostic()
    {
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderOverlapTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var holder1 = new InstitutionalHolder { Name = "Fund A" };
        var holder2 = new InstitutionalHolder { Name = "Fund B" };
        var overlap = new FundOverlapResult
        {
            UnionPositionCount = 0,
            IntersectionPositionCount = 0,
            JaccardSimilarityPercent = 0.0,
            DollarWeightedOverlapPercent = 0.0,
            Rows = new List<FundOverlapRow>(),
        };

        var rendered = (string)
            method.Invoke(null, [holder1, holder2, new DateOnly(2024, 9, 30), overlap, 20]);

        rendered.Should().Contain("Neither fund reports any positions for this date.");
    }
}
