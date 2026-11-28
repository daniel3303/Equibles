using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class TableNormalizationStepArialPreWrapEmptyCellTests
{
    // A recurring SEC filer artifact: a pre-wrap Arial span holding one space pads a visually empty cell.
    [Fact]
    public void Execute_RowWithOnlyArialPreWrapSpanCell_RemovesRow()
    {
        var parser = new HtmlParser(
            new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }
        );
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td><span style=\"white-space:pre-wrap;font-family:Arial;font-kerning:none;min-width:fit-content;\"> </span></td></tr>"
                + "<tr><td>Real content</td></tr>"
                + "</table></body></html>"
        );
        var step = new TableNormalizationStep();

        step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Length.Should().Be(1);
        rows[0].TextContent.Trim().Should().Be("Real content");
    }
}
