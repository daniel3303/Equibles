using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class TableNormalizationStepRemoveEmptyColumnsMultiTests
{
    // Removing MULTIPLE non-adjacent empty columns is the index-shift trap: the
    // existing pin removes a single column. With empties at columns 1 and 3,
    // a naive ascending removal would shift indices and delete the wrong cells.
    // Contract: every empty column is dropped and the non-empty columns survive
    // intact, in order. Isolated via reflection — this is Execute's last step.
    [Fact]
    public void RemoveEmptyColumns_TwoNonAdjacentEmptyColumns_RemovesBothKeepingOrder()
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>A1</td><td></td><td>C1</td><td></td><td>E1</td></tr>"
                + "<tr><td>A2</td><td></td><td>C2</td><td></td><td>E2</td></tr>"
                + "</table></body></html>"
        );
        var table = doc.QuerySelector("table");
        var step = new TableNormalizationStep(parser);
        var method = typeof(TableNormalizationStep).GetMethod(
            "RemoveEmptyColumns",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        method.Invoke(step, [table]);

        var rows = table.QuerySelectorAll("tr").ToList();
        rows[0].QuerySelectorAll("td").Select(c => c.TextContent).Should().Equal("A1", "C1", "E1");
        rows[1].QuerySelectorAll("td").Select(c => c.TextContent).Should().Equal("A2", "C2", "E2");
    }
}
