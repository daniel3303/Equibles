using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class TableNormalizationStepHeaderOverFiguresTests
{
    private readonly HtmlParser _parser = new(
        new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }
    );

    [Fact]
    public void Execute_HeaderSpanningSpacerAndFigures_MovesHeaderOverFigures()
    {
        var doc = _parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td></td><td>2026</td><td>2025</td><td colspan=\"2\">% Change</td></tr>"
                + "<tr><td>Revenue</td><td>7,741,187</td><td>7,646,739</td><td><p>&nbsp;</p></td><td>1.2</td></tr>"
                + "<tr><td>Costs of revenue</td><td>(6,098,403)</td><td>(6,366,621)</td><td><p>&nbsp;</p></td><td>(4.2)</td></tr>"
                + "</table></body></html>"
        );

        new TableNormalizationStep().Execute(doc);

        Cells(doc, 0).Should().Equal("", "2026", "2025", "% Change");
        Cells(doc, 1).Should().Equal("Revenue", "7,741,187", "7,646,739", "1.2");
        doc.QuerySelectorAll("[data-colspan-fill]").Should().BeEmpty();
    }

    [Fact]
    public void Execute_HeaderOverAnEmptyColumnOfItsOwn_StaysInPlace()
    {
        var doc = _parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td></td><td>Primo</td><td></td><td>Secondo stadio</td></tr>"
                + "<tr><td></td><td>Stadio</td><td>di cui: basso rischio</td><td></td></tr>"
                + "<tr><td>Finanziamenti</td><td>3,137,333</td><td></td><td>22,627</td></tr>"
                + "</table></body></html>"
        );

        new TableNormalizationStep().Execute(doc);

        Cells(doc, 1).Should().Equal("", "Stadio", "di cui: basso rischio", "");
        Cells(doc, 2).Should().Equal("Finanziamenti", "3,137,333", "", "22,627");
    }

    private static IEnumerable<string> Cells(AngleSharp.Html.Dom.IHtmlDocument doc, int row) =>
        doc.QuerySelectorAll("tr")[row].QuerySelectorAll("td").Select(cell => cell.TextContent);
}
