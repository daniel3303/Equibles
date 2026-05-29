using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class TableNormalizationStepFixRowspanMultiRowTests
{
    // FixRowspan must drop a placeholder into EACH of the next N-1 rows at the
    // spanned column. Every existing rowspan pin uses rowspan=2 (one following
    // row), so the loop's second-and-later iterations are unexercised — a bug
    // that stops after the first row, or mis-advances `nextRow`, would slip
    // through. Isolated via reflection so Execute's later empty-row/column
    // removal doesn't delete the placeholders before they can be asserted.
    [Fact]
    public void FixRowspan_RowspanOfThree_InsertsPlaceholderInBothFollowingRows()
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td rowspan=\"3\">A</td><td>B</td></tr>"
                + "<tr><td>C</td><td>D</td></tr>"
                + "<tr><td>E</td><td>F</td></tr>"
                + "</table></body></html>"
        );
        var table = doc.QuerySelector("table");
        var step = new TableNormalizationStep(parser);
        var method = typeof(TableNormalizationStep).GetMethod(
            "FixRowspan",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        method.Invoke(step, [table, doc]);

        var rows = table.QuerySelectorAll("tr").ToList();
        var secondRowCells = rows[1].QuerySelectorAll("td").ToList();
        var thirdRowCells = rows[2].QuerySelectorAll("td").ToList();

        // Both following rows gain a leading placeholder at the spanned column 0.
        secondRowCells.Should().HaveCount(3);
        secondRowCells[0].TextContent.Should().BeNullOrWhiteSpace();
        secondRowCells[1].TextContent.Should().Be("C");
        thirdRowCells.Should().HaveCount(3);
        thirdRowCells[0].TextContent.Should().BeNullOrWhiteSpace();
        thirdRowCells[1].TextContent.Should().Be("E");
    }
}
