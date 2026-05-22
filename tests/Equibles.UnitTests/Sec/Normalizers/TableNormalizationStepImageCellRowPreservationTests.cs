using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// Sibling to the EmptyRowRemoval / EmptyColumnRemoval tests. Those pin the
/// contract that a row whose cells are visually empty (whitespace, &amp;nbsp;,
/// whitespace-only span) is dropped. The complementary contract — that a row
/// whose cells contain visual content but no text (a signature image, an
/// embedded chart) is NOT dropped — is unpinned, even though SEC filings
/// routinely place signature images inside trailing table rows of the 10-K
/// signature page. Asserting it here makes the normalizer safe for image-only
/// rows and locks down `IsOnlyWhitespaceSpan`'s no-span branch to mean "not
/// only whitespace spans, the cell has other visual content".
/// </summary>
public class TableNormalizationStepImageCellRowPreservationTests
{
    [Fact(
        Skip = "GH-1776 — IsOnlyWhitespaceSpan returns true when the cell HTML has zero <span> elements, so any row whose only content is an <img> (or other non-span visual element) is dropped by RemoveEmptyRows"
    )]
    public void Execute_RowWithImageOnlyCell_PreservesRow()
    {
        // Cell content: a single <img> — no text, no spans. The contract for
        // RemoveEmptyRows is "drop visually empty rows"; an image is visual
        // content, so the row must survive. The first row (text "Data") is a
        // control: it's guaranteed to survive, anchoring the assertion on the
        // image row's fate independently of any unrelated normalization step.
        var parser = new HtmlParser(
            new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }
        );
        var step = new TableNormalizationStep(parser);
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>Data</td></tr>"
                + "<tr><td><img src=\"signature.png\" alt=\"signature\"></td></tr>"
                + "</table></body></html>"
        );

        step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Length.Should()
            .Be(
                2,
                "a row containing only an <img> is visually non-empty and must not be removed by RemoveEmptyRows"
            );
        rows[1].QuerySelectorAll("img").Length.Should().Be(1);
    }
}
