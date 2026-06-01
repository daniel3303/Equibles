using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class CurrencyConsolidationStepWordEndingInUsDollarTests
{
    private readonly CurrencyConsolidationStep _sut = new();
    private readonly HtmlParser _parser = new();

    // Contract: only the exact "US" prefix denotes US Dollars ("US$"). A longer word
    // that merely ends in "us" before the "$" (e.g. "PLUS$") is not US Dollars — the
    // guard must match the FULL contiguous letter run, not just the trailing two chars.
    // Guards the US$ exception against over-matching; the GH-3118 repro only covers the
    // exact "US$" true positive. Oracle derived from the notation contract.
    [Fact]
    public void WordEndingInUsBeforeDollarSymbol_IsNotMistakenForUsDollars()
    {
        var html =
            @"<html><body><table>
  <tr><td>PLUS$</td><td></td><td>100</td></tr>
  <tr><td>PLUS$</td><td></td><td>200</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);

        _sut.Execute(doc);

        doc.QuerySelector("table + p em").Should().BeNull();
    }
}
