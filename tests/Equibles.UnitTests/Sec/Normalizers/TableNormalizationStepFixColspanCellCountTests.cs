using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class TableNormalizationStepFixColspanCellCountTests
{
    // FixColspan must expand colspan=N into exactly N sibling cells (the
    // original plus N-1 new empties), preserving the original's content. The
    // existing pin only checks the attribute is removed; an off-by-one in the
    // `for (i=1; i<colspanValue)` loop (too few/many cells) would pass that yet
    // corrupt the grid. Tested in isolation because Execute's later
    // RemoveEmptyColumns deletes the inserted empties, masking the count.
    [Fact]
    public void FixColspan_ColspanOfThree_ExpandsToThreeCellsPreservingContent()
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(
            "<html><body><table><tr><td colspan=\"3\">A</td></tr></table></body></html>"
        );
        var table = doc.QuerySelector("table");
        var step = new TableNormalizationStep(parser);
        var method = typeof(TableNormalizationStep).GetMethod(
            "FixColspan",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        method.Invoke(step, [table, doc]);

        var cells = table.QuerySelectorAll("td");
        cells.Length.Should().Be(3);
        cells[0].TextContent.Should().Be("A");
    }
}
