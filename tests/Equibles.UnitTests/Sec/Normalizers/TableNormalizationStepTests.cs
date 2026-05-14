using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class TableNormalizationStepTests
{
    private readonly HtmlParser _parser = new(
        new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }
    );
    private readonly TableNormalizationStep _step;

    public TableNormalizationStepTests()
    {
        _step = new TableNormalizationStep(_parser);
    }

    [Fact]
    public void Execute_NonNumericColspan_SkipsExpansionAndDoesNotThrow()
    {
        // FixColspan guards every cell with `int.TryParse(colspanAttr) || colspanValue <= 1`
        // → `continue`. The TryParse half is load-bearing: SEC filings emitted by older
        // EDGAR converters and some hand-edited submissions sometimes carry malformed
        // colspan values like "abc", "100%" (percentage-style misuse), or empty strings
        // — rare but real. The guard ensures these are silently skipped: the cell stays
        // intact, the colspan attribute is left alone (since RemoveAttribute is AFTER
        // the guard's continue), and the normalize pass marches on to the next cell.
        //
        // The risk this pins: a refactor that "modernizes" TryParse to int.Parse (or
        // adds an int.Parse-style fallback assuming the upstream HTML is well-formed)
        // would throw FormatException on the first malformed colspan in the document,
        // bubble up through the foreach, abort the whole TableNormalizationStep for
        // that filing, and either drop the entire document from the index or crash
        // DocumentScraper on the un-normalized table. The failure mode is invisible
        // to the existing colspan/rowspan tests (all use numeric values) — those
        // would stay green while a single malformed SEC filing wipes out scraping
        // for the whole period.
        //
        // Assertions: cell content survives, colspan attribute survives (proving the
        // continue fired before RemoveAttribute), no new cells were inserted (proving
        // the for loop didn't run), and no exception escaped the Execute call.
        var doc = _parser.ParseDocument(
            "<html><body><table><tr><td colspan=\"abc\">Cell</td></tr></table></body></html>"
        );

        var act = () => _step.Execute(doc);

        act.Should().NotThrow();
        var cells = doc.QuerySelectorAll("td");
        cells.Length.Should().Be(1);
        cells[0].TextContent.Should().Be("Cell");
        cells[0].GetAttribute("colspan").Should().Be("abc");
    }

    [Fact]
    public void Execute_NonNumericRowspan_SkipsExpansionAndDoesNotThrow()
    {
        // Sibling to Execute_NonNumericColspan_SkipsExpansionAndDoesNotThrow.
        // FixRowspan is structurally separate from FixColspan but uses the
        // identical defensive guard:
        //   `if (!int.TryParse(rowspanAttr, out var rowspanValue) || rowspanValue <= 1)
        //        continue;`
        // The two FixX methods don't share a helper — each TryParse branch is
        // independently load-bearing and must be pinned independently. A refactor
        // that "modernizes" only ONE of them to int.Parse (the other staying
        // TryParse) would leave the colspan pin green while crashing on
        // malformed rowspan values, and vice versa. Pin the rowspan branch on
        // a non-numeric attribute so a single-method regression is caught.
        //
        // The risk is the same shape as the colspan pin: SEC filings emitted
        // by older EDGAR converters and hand-edited submissions occasionally
        // carry malformed rowspan values ("abc", "100%", empty). The guard
        // ensures these silently skip; without it, int.Parse throws
        // FormatException, the foreach over cellsWithRowspan aborts, and the
        // whole TableNormalizationStep bails out on that filing — dropping
        // the document or crashing DocumentScraper.
        //
        // Assertions mirror the colspan pin: cell content survives, rowspan
        // attribute stays intact (proving the continue fired before
        // RemoveAttribute), no new rows are inserted (proving the for loop
        // didn't run), no exception escapes.
        var doc = _parser.ParseDocument(
            "<html><body><table><tr><td rowspan=\"abc\">Cell</td></tr></table></body></html>"
        );

        var act = () => _step.Execute(doc);

        act.Should().NotThrow();
        var cells = doc.QuerySelectorAll("td");
        cells.Length.Should().Be(1);
        cells[0].TextContent.Should().Be("Cell");
        cells[0].GetAttribute("rowspan").Should().Be("abc");
    }

    [Fact]
    public void Execute_ColspanExpansion_RemovesColspanAttribute()
    {
        var doc = _parser.ParseDocument(
            "<html><body><table><tr><td colspan=\"3\">A</td></tr></table></body></html>"
        );

        _step.Execute(doc);

        // Colspan attribute should be removed after processing
        var colspanCells = doc.QuerySelectorAll("td[colspan]");
        colspanCells.Length.Should().Be(0);

        // The original cell content is preserved
        var cells = doc.QuerySelectorAll("td");
        cells[0].TextContent.Should().Be("A");
    }

    [Fact]
    public void Execute_RowspanExpansion_RemovesRowspanAttribute()
    {
        var doc = _parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td rowspan=\"2\">A</td><td>B</td></tr>"
                + "<tr><td>C</td></tr>"
                + "</table></body></html>"
        );

        _step.Execute(doc);

        // Rowspan attribute should be removed after processing
        var rowspanCells = doc.QuerySelectorAll("td[rowspan]");
        rowspanCells.Length.Should().Be(0);

        // Both rows should still be present
        var rows = doc.QuerySelectorAll("tr");
        rows.Length.Should().Be(2);

        // Original cell content is preserved
        var firstRowCells = rows[0].QuerySelectorAll("td");
        firstRowCells[0].TextContent.Should().Be("A");
        firstRowCells[1].TextContent.Should().Be("B");
    }

    [Fact]
    public void Execute_EmptyRowRemoval_RemovesRowWithOnlyWhitespaceAndNbsp()
    {
        var doc = _parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>Data</td></tr>"
                + "<tr><td>&nbsp;</td></tr>"
                + "<tr><td>   </td></tr>"
                + "</table></body></html>"
        );

        _step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Length.Should().Be(1);
        rows[0].QuerySelectorAll("td")[0].TextContent.Should().Be("Data");
    }

    [Fact]
    public void Execute_NonEmptyRowPreserved_KeepsRowsWithTextContent()
    {
        var doc = _parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>Row 1</td></tr>"
                + "<tr><td>Row 2</td></tr>"
                + "</table></body></html>"
        );

        _step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Length.Should().Be(2);
    }

    [Fact]
    public void Execute_EmptyColumnRemoval_RemovesColumnWhereAllCellsAreEmpty()
    {
        var doc = _parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>A</td><td>&nbsp;</td><td>B</td></tr>"
                + "<tr><td>C</td><td> </td><td>D</td></tr>"
                + "</table></body></html>"
        );

        _step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Length.Should().Be(2);

        var firstRowCells = rows[0].QuerySelectorAll("td");
        firstRowCells.Length.Should().Be(2);
        firstRowCells[0].TextContent.Should().Be("A");
        firstRowCells[1].TextContent.Should().Be("B");

        var secondRowCells = rows[1].QuerySelectorAll("td");
        secondRowCells.Length.Should().Be(2);
        secondRowCells[0].TextContent.Should().Be("C");
        secondRowCells[1].TextContent.Should().Be("D");
    }

    [Fact]
    public void Execute_MixedTable_RemovesBothColspanAndRowspanAttributes()
    {
        var doc = _parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td colspan=\"2\">Header</td><td>X</td></tr>"
                + "<tr><td rowspan=\"2\">Left</td><td>M1</td><td>M2</td></tr>"
                + "<tr><td>N1</td><td>N2</td></tr>"
                + "</table></body></html>"
        );

        _step.Execute(doc);

        // All colspan and rowspan attributes should be removed
        doc.QuerySelectorAll("[colspan]").Length.Should().Be(0);
        doc.QuerySelectorAll("[rowspan]").Length.Should().Be(0);

        // All three rows should be preserved (they have content)
        var rows = doc.QuerySelectorAll("tr");
        rows.Length.Should().Be(3);

        // Original cell content is preserved across all rows
        var allCells = doc.QuerySelectorAll("td");
        allCells.Should().Contain(c => c.TextContent == "Header");
        allCells.Should().Contain(c => c.TextContent == "X");
        allCells.Should().Contain(c => c.TextContent == "Left");
        allCells.Should().Contain(c => c.TextContent == "M1");
        allCells.Should().Contain(c => c.TextContent == "M2");
        allCells.Should().Contain(c => c.TextContent == "N1");
        allCells.Should().Contain(c => c.TextContent == "N2");
    }

    [Fact]
    public void Execute_EmptyRowRemoval_RemovesRowContainingOnlyWhitespaceSpan()
    {
        // SEC filings frequently wrap whitespace in styled spans for layout
        // (e.g. <span style="color:red"> </span>). Those rows are visually
        // empty and should be stripped even though the cell's InnerHtml is
        // not literally whitespace or &nbsp;.
        var doc = _parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>Data</td></tr>"
                + "<tr><td><span style=\"color:red\"> </span></td></tr>"
                + "</table></body></html>"
        );

        _step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Length.Should().Be(1);
        rows[0].QuerySelectorAll("td")[0].TextContent.Should().Be("Data");
    }

    [Fact]
    public void Execute_RowspanInTrailingColumnOverShorterRow_AppendsEmptyCellAtRowEnd()
    {
        // When a rowspan in the LAST column of one row crosses into a subsequent
        // row that has FEWER cells, the column index used to splice in the empty
        // padding cell is past the end of that shorter row's cell list. The
        // InsertEmptyCellAtIndex helper handles the two cases differently:
        // `cellIndex < existingCells.Count` calls `InsertBefore(... existingCells[cellIndex])`,
        // while the else-branch falls back to `AppendChild(newCell)`. The existing
        // rowspan tests only exercise the in-range InsertBefore branch (rowspan in
        // column 0 with both rows two cells wide). Without this pin, a refactor
        // that removed the AppendChild fallback — or that threw
        // ArgumentOutOfRangeException on the missing index — would silently corrupt
        // SEC tables whose sparse trailing rows have fewer columns than the rowspan
        // header (a common pattern in 10-K footnote tables, where a "(continued)"
        // row only fills one or two columns). The downstream consumer counts the
        // cells per row to align headers with data; a missing pad cell shifts the
        // last column out of alignment for the rest of the table.
        //
        // Setup: row 1 has [A, B, C] with C declaring rowspan=2; row 2 has just [D].
        // After normalization, row 2 must end up with TWO cells — D at index 0 and
        // an empty appended cell — and the rowspan attribute on C must be gone.
        var doc = _parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>A</td><td>B</td><td rowspan=\"2\">C</td></tr>"
                + "<tr><td>D</td></tr>"
                + "</table></body></html>"
        );

        _step.Execute(doc);

        doc.QuerySelectorAll("[rowspan]").Length.Should().Be(0);

        var rows = doc.QuerySelectorAll("tr");
        rows.Length.Should().Be(2);

        var secondRowCells = rows[1].QuerySelectorAll("td");
        secondRowCells.Length.Should().Be(2);
        secondRowCells[0].TextContent.Should().Be("D");
        secondRowCells[1].TextContent.Should().BeEmpty();
    }

    [Fact]
    public void Execute_NoTables_DoesNotThrow()
    {
        var doc = _parser.ParseDocument("<html><body><p>No tables here</p></body></html>");

        var act = () => _step.Execute(doc);

        act.Should().NotThrow();
    }
}
