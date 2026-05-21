using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderSectorAllocationTableEmptySlicesTests
{
    private static readonly MethodInfo RenderSectorAllocationTableMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderSectorAllocationTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // RenderSectorAllocationTable (extracted in #1549) early-returns with a
    // placeholder when the slices list is empty — the holder reported nothing
    // this quarter. A refactor that dropped the empty-slice shortcut would
    // emit the "| # | Industry | ... |" header followed by no body rows,
    // producing a confusing zero-row markdown table that the LLM might
    // mistake for a successful empty-portfolio response.
    [Fact]
    public void RenderSectorAllocationTable_EmptySlices_EmitsPlaceholderWithoutTableHeader()
    {
        var holder = new InstitutionalHolder { Name = "Test Fund" };
        var slices = new List<IndustryAllocationSlice>();

        var rendered = (string)
            RenderSectorAllocationTableMethod.Invoke(
                null,
                [holder, new DateOnly(2024, 9, 30), slices]
            );

        rendered.Should().Contain("_No holdings reported for the selected quarter._");
        rendered.Should().NotContain("| # | Industry |");
    }
}
