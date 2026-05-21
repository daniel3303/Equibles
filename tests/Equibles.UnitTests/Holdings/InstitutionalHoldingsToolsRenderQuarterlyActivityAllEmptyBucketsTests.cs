using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderQuarterlyActivityAllEmptyBucketsTests
{
    private static readonly MethodInfo RenderQuarterlyActivityMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderQuarterlyActivity",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // RenderQuarterlyActivity (extracted in #1582) counts how many section
    // buckets contained at least one row; if the running total stays at 0
    // after walking every selected section, it appends a "_No matching
    // buckets._" line. The count is driven by AppendActivitySection's
    // return value (false when rows.Count == 0), so a refactor that
    // incremented `rendered` on section iteration instead of on non-empty
    // body would silently suppress the fallback — leaving the LLM with
    // four "No stocks in this bucket" placeholders and no overall
    // signal that the holder didn't move at all this quarter.
    [Fact]
    public void RenderQuarterlyActivity_AllFourBucketsEmpty_AppendsNoMatchingBucketsFallback()
    {
        var holder = new InstitutionalHolder { Name = "Test Fund" };
        var grouped = new Dictionary<StockPositionChangeType, List<StockPositionChange>>
        {
            [StockPositionChangeType.Initiated] = [],
            [StockPositionChangeType.Increased] = [],
            [StockPositionChangeType.Reduced] = [],
            [StockPositionChangeType.Exited] = [],
        };

        var rendered = (string)
            RenderQuarterlyActivityMethod.Invoke(
                null,
                [holder, new DateOnly(2024, 9, 30), new DateOnly(2024, 6, 30), grouped, "", 20]
            );

        rendered.Should().Contain("_No matching buckets._");
    }
}
