using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HtmlElementExtensionsTests {
    [Fact]
    public void DirectChildCells_RowMixingTdAndTh_ReturnsBothTagsInDocumentOrder() {
        // DirectChildCells filters a `<tr>`'s direct children to the
        // OR-pattern `c.LocalName is "td" or "th"`. The TableNormalizationStep
        // tests exercise this helper indirectly through full-table normalization,
        // but those tests all use rows containing ONLY `<td>` cells — the `th`
        // arm of the OR is never independently exercised.
        //
        // The risk this catches: a refactor that "tidies up" the OR to just
        // `LocalName == "td"` — under the false intuition that header rows are
        // a corner case and "real SEC financial tables only use td anyway" —
        // would compile cleanly, pass every existing TableNormalizationStep
        // pin, and silently break two production paths:
        //
        // 1) Header-cell rowspan/colspan expansion. The FixColspan/FixRowspan
        //    code in TableNormalizationStep walks `td[colspan], th[colspan]`
        //    via CSS selector to FIND header cells, then calls
        //    DirectChildCells on the parent row to compute the index for
        //    InsertEmptyCellAtIndex. If DirectChildCells loses the `th`
        //    branch, the index points at the wrong column position —
        //    cells get inserted in the body row at the wrong slot, shifting
        //    every subsequent column rightward by one. SEC financial
        //    statements with merged header cells (common in 10-K balance
        //    sheets where "Three Months Ended" spans three quarter columns)
        //    would render with misaligned data.
        //
        // 2) Empty-column detection. IsColumnEmpty in RemoveEmptyColumns
        //    walks DirectChildCells per row to find a cell at the target
        //    column index. Dropping `th` makes header rows appear shorter
        //    than body rows, breaking the alignment that the per-column
        //    walk depends on — empty-column-removal would either skip
        //    legitimate empty columns (the header row claims `cells.Count`
        //    too low) or remove columns that actually carry header text.
        //
        // Pin a row that has both `td` and `th` children. Asserting BOTH
        // the count (2) and the LocalName sequence (`th` first then `td`)
        // proves (a) both arms of the OR fired AND (b) document order was
        // preserved. A regression that swapped the arms via `c.LocalName
        // is "th" or "td"` would still return both cells in document order,
        // so the LocalName assertion plus count is the minimal pin that
        // catches an arm drop without false-failing on a cosmetic OR
        // reordering.
        var doc = new HtmlParser().ParseDocument(
            "<html><body><table><tr id=\"row\"><th>Label</th><td>Value</td></tr></table></body></html>");
        var row = doc.GetElementById("row")!;

        var cells = HtmlElementExtensions.DirectChildCells(row);

        cells.Should().HaveCount(2);
        cells[0].LocalName.Should().Be("th");
        cells[1].LocalName.Should().Be("td");
    }

    [Fact]
    public void InsertAfter_RefNodeIsLastChild_AppendsAtEnd() {
        var doc = new HtmlParser().ParseDocument("<html><body><div id=\"p\"><span id=\"first\"></span></div></body></html>");
        var parent = doc.GetElementById("p")!;
        var refNode = doc.GetElementById("first")!;
        var newNode = doc.CreateElement("em");
        newNode.SetAttribute("id", "added");

        HtmlElementExtensions.InsertAfter(parent, newNode, refNode);

        parent.LastElementChild!.GetAttribute("id").Should().Be("added");
    }
}
