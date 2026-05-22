using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// Column-side mirror of <see cref="TableNormalizationStepImageCellRowPreservationTests"/>.
/// `RemoveEmptyRows` was fixed under GH-1776 to keep rows whose cells carry
/// non-text visual content (e.g. an <c>&lt;img&gt;</c>). The complementary
/// contract for `RemoveEmptyColumns` — drop only *visually* empty columns,
/// not every column whose cells happen to have empty <c>TextContent</c> — is
/// unpinned. `IsColumnEmpty` looks at `TextContent` only, so a column whose
/// cells contain only an <c>&lt;img&gt;</c> is dropped exactly the same way
/// the row path used to drop image-only rows.
/// </summary>
public class TableNormalizationStepImageOnlyColumnPreservationTests
{
    [Fact(
        Skip = "GH-1797 — IsColumnEmpty looks at cell.TextContent only; a column whose cells each contain only an <img> (or other non-text visual element) is dropped by RemoveEmptyColumns"
    )]
    public void Execute_ColumnWhereEveryCellIsImageOnly_PreservesColumn()
    {
        // The right column on every row is a single <img> — no text, no
        // spans. The left column carries text ("A"/"C") so the rows survive
        // RemoveEmptyRows; the assertion then isolates the column path. The
        // contract for RemoveEmptyColumns is "drop visually empty columns";
        // an image column is visual content and must survive.
        var parser = new HtmlParser(
            new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }
        );
        var step = new TableNormalizationStep(parser);
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>A</td><td><img src=\"sig1.png\" alt=\"signature\"></td></tr>"
                + "<tr><td>C</td><td><img src=\"sig2.png\" alt=\"signature\"></td></tr>"
                + "</table></body></html>"
        );

        step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Length.Should().Be(2, "the text column keeps both rows alive");

        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("td");
            cells
                .Length.Should()
                .Be(
                    2,
                    "a column whose cells each contain an <img> is visually non-empty and must not be removed by RemoveEmptyColumns"
                );
            cells[1].QuerySelectorAll("img").Length.Should().Be(1);
        }
    }
}
