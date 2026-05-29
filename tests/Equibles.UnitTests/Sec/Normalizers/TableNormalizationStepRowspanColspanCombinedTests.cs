using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class TableNormalizationStepRowspanColspanCombinedTests
{
    // A cell with BOTH rowspan and colspan occupies a rectangular block, so the
    // normalizer must expand it into that full block — leaving every row with the
    // same column count and trailing cells column-aligned. Existing pins cover
    // colspan and rowspan in isolation; this pins their combination, where the
    // colspan width must also propagate down the rowspan-spanned rows.
    [Fact(Skip = "GH-2801 — rowspan+colspan cell expands to a ragged grid")]
    public void Execute_CellWithBothRowspanAndColspan_ProducesRectangularGrid()
    {
        // Cell A spans rows 0-1 and cols 0-1; B sits at col 2 of row 0, so C
        // (the only cell of row 1) belongs at col 2, directly under B.
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td rowspan=\"2\" colspan=\"2\">A</td><td>B</td></tr>"
                + "<tr><td>C</td></tr>"
                + "</table></body></html>"
        );
        var step = new TableNormalizationStep(parser);

        step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Should().HaveCount(2);

        var firstRowCells = rows[0].QuerySelectorAll("td").Length;
        var secondRowCells = rows[1].QuerySelectorAll("td").Length;

        // Rectangular grid: every row carries the same column count once spans
        // are expanded. A ragged result means the colspan width was not carried
        // into the rowspan-spanned row, misaligning C from B.
        secondRowCells.Should().Be(firstRowCells);
    }
}
