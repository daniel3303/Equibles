using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class CurrencyConsolidationStepForeignDollarPrefixedSymbolTests
{
    private readonly CurrencyConsolidationStep _sut = new();
    private readonly HtmlParser _parser = new();

    // Contract (ContainsCurrencySymbol doc): "A symbol immediately preceded by a letter is a
    // different currency's prefixed symbol (e.g. C$, A$, HK$, NZ$), not this one — so a
    // Canadian/Australian/HK dollar cell is not mistaken for US Dollars." A "C$" currency column
    // must therefore be left alone: no consolidation, and no "All values are in US Dollars." note.
    // Oracle derived from the contract; this pins the positive side of the letter-prefix guard
    // (the GH-3118 repro only covers the "US$" false negative).
    [Fact]
    public void ForeignDollarPrefixedSymbolColumn_IsNotMistakenForUsDollars()
    {
        var html =
            @"<html><body><table>
  <tr><td>C$</td><td></td><td>100</td></tr>
  <tr><td>C$</td><td></td><td>200</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);

        _sut.Execute(doc);

        doc.QuerySelector("table + p em").Should().BeNull();
        doc.QuerySelectorAll("td").Select(td => td.TextContent).Should().Contain("C$");
    }
}
