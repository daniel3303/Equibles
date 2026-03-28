using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.Tests.Sec.Normalizers;

public class TableNormalizationStepTests {
    private readonly HtmlParser _parser = new(new HtmlParserOptions {
        IsAcceptingCustomElementsEverywhere = true
    });
    private readonly TableNormalizationStep _step;

    public TableNormalizationStepTests() {
        _step = new TableNormalizationStep(_parser);
    }

    [Fact]
    public void Execute_ColspanExpansion_RemovesColspanAttribute() {
        var doc = _parser.ParseDocument(
            "<html><body><table><tr><td colspan=\"3\">A</td></tr></table></body></html>");

        _step.Execute(doc);

        // Colspan attribute should be removed after processing
        var colspanCells = doc.QuerySelectorAll("td[colspan]");
        colspanCells.Length.Should().Be(0);

        // The original cell content is preserved
        var cells = doc.QuerySelectorAll("td");
        cells[0].TextContent.Should().Be("A");
    }

    [Fact]
    public void Execute_RowspanExpansion_RemovesRowspanAttribute() {
        var doc = _parser.ParseDocument(
            "<html><body><table>" +
            "<tr><td rowspan=\"2\">A</td><td>B</td></tr>" +
            "<tr><td>C</td></tr>" +
            "</table></body></html>");

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
    public void Execute_EmptyRowRemoval_RemovesRowWithOnlyWhitespaceAndNbsp() {
        var doc = _parser.ParseDocument(
            "<html><body><table>" +
            "<tr><td>Data</td></tr>" +
            "<tr><td>&nbsp;</td></tr>" +
            "<tr><td>   </td></tr>" +
            "</table></body></html>");

        _step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Length.Should().Be(1);
        rows[0].QuerySelectorAll("td")[0].TextContent.Should().Be("Data");
    }

    [Fact]
    public void Execute_NonEmptyRowPreserved_KeepsRowsWithTextContent() {
        var doc = _parser.ParseDocument(
            "<html><body><table>" +
            "<tr><td>Row 1</td></tr>" +
            "<tr><td>Row 2</td></tr>" +
            "</table></body></html>");

        _step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Length.Should().Be(2);
    }

    [Fact]
    public void Execute_EmptyColumnRemoval_RemovesColumnWhereAllCellsAreEmpty() {
        var doc = _parser.ParseDocument(
            "<html><body><table>" +
            "<tr><td>A</td><td>&nbsp;</td><td>B</td></tr>" +
            "<tr><td>C</td><td> </td><td>D</td></tr>" +
            "</table></body></html>");

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
    public void Execute_MixedTable_RemovesBothColspanAndRowspanAttributes() {
        var doc = _parser.ParseDocument(
            "<html><body><table>" +
            "<tr><td colspan=\"2\">Header</td><td>X</td></tr>" +
            "<tr><td rowspan=\"2\">Left</td><td>M1</td><td>M2</td></tr>" +
            "<tr><td>N1</td><td>N2</td></tr>" +
            "</table></body></html>");

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
    public void Execute_NoTables_DoesNotThrow() {
        var doc = _parser.ParseDocument(
            "<html><body><p>No tables here</p></body></html>");

        var act = () => _step.Execute(doc);

        act.Should().NotThrow();
    }
}
