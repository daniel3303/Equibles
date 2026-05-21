using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderOverlapTableZeroSharesTests
{
    private static readonly MethodInfo RenderOverlapTableMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderOverlapTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // RenderOverlapTable (extracted in #1548) renders per-stock rows with
    // strict-greater conditionals: `a.Shares > 0` and `a.Value > 0` decide
    // whether the cell shows the number or an em-dash placeholder. When a
    // stock is held by fund B but not by fund A, the A slice arrives with
    // `Shares = 0` (the FundOverlapCalculator emits zero for the absent
    // side), and the row must show "—" in both the A Shares AND A %
    // columns. A regression flipping the comparison from `> 0` to `>= 0`
    // would render "0" / "0.0%" instead of "—" — silently asserting that
    // fund A holds zero shares, rather than no position at all.
    [Fact]
    public void RenderOverlapTable_FundAHasZeroSharesFundBHasPosition_RendersDashPlaceholderForFundA()
    {
        var holder1 = new InstitutionalHolder { Name = "Fund A" };
        var holder2 = new InstitutionalHolder { Name = "Fund B" };
        var selected = new DateOnly(2024, 9, 30);
        var overlap = new FundOverlapResult
        {
            UnionPositionCount = 1,
            IntersectionPositionCount = 0,
            JaccardSimilarityPercent = 0.0,
            DollarWeightedOverlapPercent = 0.0,
            Rows =
            [
                new FundOverlapRow
                {
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    CombinedValue = 1_000_000_000L,
                    Slices =
                    [
                        new()
                        {
                            Shares = 0,
                            Value = 0,
                            PercentOfPortfolio = 0.0,
                        },
                        new()
                        {
                            Shares = 100_000,
                            Value = 1_000_000_000L,
                            PercentOfPortfolio = 12.3,
                        },
                    ],
                },
            ],
        };

        var rendered = (string)
            RenderOverlapTableMethod.Invoke(null, [holder1, holder2, selected, overlap, 20]);

        // The A Shares + A % pair renders as two consecutive pipe-anchored "—"
        // cells when fund A holds no position in the stock. Culture-sensitive
        // number formatting on the B side is a separate concern (rendered via
        // `ToString("N0")` without InvariantCulture) — only assert the dash pin.
        rendered.Should().Contain("| — | — |");
    }
}
