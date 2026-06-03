using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class TableNormalizationStepFixRowspanExceedsRowsTests
{
    // SEC HTML routinely carries a rowspan larger than the rows that actually
    // follow. FixRowspan loops rowspan-1 times advancing `nextRow`; once the rows
    // run out it must stop cleanly (the `nextRow != null` guard) rather than throw
    // when there is no row left to receive a placeholder. Every existing rowspan
    // pin keeps rowspan within the available rows, so the exhaustion path is
    // unexercised. Contract: fill the rows that exist, ignore the phantom
    // remainder, and strip the rowspan attribute regardless. Isolated via
    // reflection so Execute's later empty-row/column removal can't touch the result.
    [Fact]
    public void FixRowspan_RowspanLargerThanFollowingRows_FillsAvailableRowAndDoesNotThrow()
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td rowspan=\"4\">A</td><td>B</td></tr>"
                + "<tr><td>C</td><td>D</td></tr>"
                + "</table></body></html>"
        );
        var table = doc.QuerySelector("table");
        var step = new TableNormalizationStep(parser);
        var method = typeof(TableNormalizationStep).GetMethod(
            "FixRowspan",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var invoke = () => method.Invoke(step, [table, doc]);

        invoke.Should().NotThrow();

        var spannedCell = table.QuerySelector("td");
        spannedCell.HasAttribute("rowspan").Should().BeFalse();

        var rows = table.QuerySelectorAll("tr").ToList();
        var secondRowCells = rows[1].QuerySelectorAll("td").ToList();
        secondRowCells.Should().HaveCount(3);
        secondRowCells[0].TextContent.Should().BeNullOrWhiteSpace();
        secondRowCells[1].TextContent.Should().Be("C");
    }
}
