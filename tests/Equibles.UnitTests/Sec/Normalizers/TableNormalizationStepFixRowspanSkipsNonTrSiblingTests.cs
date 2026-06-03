using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class TableNormalizationStepFixRowspanSkipsNonTrSiblingTests
{
    // FixRowspan walks element siblings looking for the next row to receive a
    // placeholder, and a `while` loop skips any sibling that is not a <tr>. SEC
    // table markup keeps non-row elements between rows — AngleSharp preserves a
    // <script> inside <tbody> as a sibling of the <tr>s. The placeholder must land
    // in the real next <tr>, never in the intervening element. Every existing
    // rowspan pin has clean <tr>-only siblings, so the skip loop is unexercised.
    // Isolated via reflection so Execute's later empty-row/column removal can't
    // touch the result.
    [Fact]
    public void FixRowspan_NonTrElementBetweenRows_SkipsItAndFillsTheNextRow()
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(
            "<html><body><table><tbody>"
                + "<tr><td rowspan=\"2\">A</td><td>B</td></tr>"
                + "<script>x=1</script>"
                + "<tr><td>C</td></tr>"
                + "</tbody></table></body></html>"
        );
        var table = doc.QuerySelector("table");
        var step = new TableNormalizationStep(parser);
        var method = typeof(TableNormalizationStep).GetMethod(
            "FixRowspan",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        method.Invoke(step, [table, doc]);

        // The placeholder went into the real next <tr>, not the <script> sibling.
        var dataRow = table.QuerySelectorAll("tr").Last();
        var dataCells = dataRow.QuerySelectorAll("td").ToList();
        dataCells.Should().HaveCount(2);
        dataCells[0].TextContent.Should().BeNullOrWhiteSpace();
        dataCells[1].TextContent.Should().Be("C");

        // The intervening element was left untouched (no cell inserted into it).
        table.QuerySelector("script")!.QuerySelectorAll("td").Should().BeEmpty();
    }
}
