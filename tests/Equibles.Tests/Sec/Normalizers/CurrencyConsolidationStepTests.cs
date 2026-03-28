using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.Tests.Sec.Normalizers;

public class CurrencyConsolidationStepTests
{
    private readonly CurrencyConsolidationStep _sut = new();
    private readonly HtmlParser _parser = new();

    [Fact]
    public void DollarColumnFollowedByEmptyColumn_RemovesCurrencyColumnAndAddsNote()
    {
        var html = @"<html><body><table>
  <tr><td>$</td><td></td><td>100</td></tr>
  <tr><td>$</td><td></td><td>200</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);

        _sut.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("td");
            cells.Should().HaveCount(2);
            cells[0].TextContent.Trim().Should().BeEmpty();
        }

        var note = doc.QuerySelector("table + p em");
        note.Should().NotBeNull();
        note.TextContent.Should().Be("All values are in US Dollars.");
    }

    [Fact]
    public void TableWithoutCurrencySymbols_NoChanges()
    {
        var html = @"<html><body><table>
  <tr><td>Name</td><td>Value</td></tr>
  <tr><td>Apple</td><td>100</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);
        var originalHtml = doc.DocumentElement.OuterHtml;

        _sut.Execute(doc);

        doc.DocumentElement.OuterHtml.Should().Be(originalHtml);
    }

    [Fact]
    public void EurColumnFollowedByEmptyColumn_AddsEuroNote()
    {
        var html = @"<html><body><table>
  <tr><td>€</td><td></td><td>500</td></tr>
  <tr><td>€</td><td></td><td>600</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);

        _sut.Execute(doc);

        var note = doc.QuerySelector("table + p em");
        note.Should().NotBeNull();
        note.TextContent.Should().Be("All values are in Euros.");
    }

    [Fact]
    public void DocumentWithNoTables_DoesNotThrow()
    {
        var html = @"<html><body><p>No tables here.</p></body></html>";

        var doc = _parser.ParseDocument(html);

        var act = () => _sut.Execute(doc);

        act.Should().NotThrow();
    }

    [Fact]
    public void CurrencySymbolIsRemovedFromConsolidatedText()
    {
        var html = @"<html><body><table>
  <tr><td>$</td><td></td><td>100</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);

        _sut.Execute(doc);

        var allCellTexts = doc.QuerySelectorAll("td")
            .Select(c => c.TextContent)
            .ToList();

        allCellTexts.Should().NotContain(t => t.Contains("$"));
    }
}
