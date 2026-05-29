using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class CurrencyConsolidationStepMultipleCurrencyColumnsTests
{
    private readonly CurrencyConsolidationStep _sut = new();
    private readonly HtmlParser _parser = new();

    [Fact]
    public void Execute_TableHasTwoSeparateCurrencyColumns_ConsolidatesBothWithoutIndexCorruption()
    {
        // Every existing pin in this folder uses a SINGLE currency column. This one
        // exercises the multi-column path: two currency-bearing columns (col 0 and
        // col 2), each followed by an empty separator column. ProcessCurrencyColumns-
        // ForConsolidation removes the currency column and merges its symbol-stripped
        // text into the following empty cell.
        //
        // The contract under test: with TWO such pairs, BOTH must consolidate and
        // every value must survive. That only holds because the production code
        // processes the collected columns in DESCENDING index order — removing the
        // right column (col 2) before the left (col 0) so the earlier removal can't
        // shift the still-pending column's index. If a refactor flipped that to
        // ascending order, removing col 0 first would slide €50 from col 2 into col 1
        // and the empty cell into col 2; the subsequent col-2 pass would then see an
        // empty current cell, skip it, and leave the € symbol intact — a silent
        // half-consolidation. This pin fails on that regression.
        var html =
            @"<html><body><table>
  <tr><td>$100</td><td></td><td>&#8364;50</td><td></td></tr>
  <tr><td>$200</td><td></td><td>&#8364;60</td><td></td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);

        _sut.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Should().HaveCount(2);

        // Both currency columns gone: 4 columns collapse to 2 in every row.
        foreach (var row in rows)
        {
            row.QuerySelectorAll("td").Should().HaveCount(2);
        }

        // No currency glyph survives anywhere in the consolidated table.
        var allCellTexts = doc.QuerySelectorAll("td").Select(c => c.TextContent).ToList();
        allCellTexts.Should().NotContain(t => t.Contains('$'));
        allCellTexts.Should().NotContain(t => t.Contains('€'));

        // The numeric values are preserved in order, merged into the surviving cells.
        var firstRow = rows[0].QuerySelectorAll("td").Select(c => c.TextContent.Trim()).ToList();
        firstRow.Should().Equal("100", "50");

        var secondRow = rows[1].QuerySelectorAll("td").Select(c => c.TextContent.Trim()).ToList();
        secondRow.Should().Equal("200", "60");
    }
}
