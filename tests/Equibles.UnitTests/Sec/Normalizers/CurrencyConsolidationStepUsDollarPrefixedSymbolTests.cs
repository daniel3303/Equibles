using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class CurrencyConsolidationStepUsDollarPrefixedSymbolTests
{
    private readonly CurrencyConsolidationStep _sut = new();
    private readonly HtmlParser _parser = new();

    // Contract: the step exists to detect a table's reporting currency and label it.
    // "US$" is the standard, unambiguous notation for US Dollars in SEC filings
    // (foreign private issuers and prospectuses routinely write "US$1,000"), exactly
    // equivalent to the already-pinned "$" and "USD" cells which both yield the
    // "All values are in US Dollars." note. The letter-prefix guard in
    // ContainsCurrencySymbol was written to drop OTHER currencies' prefixed glyphs
    // (C$, A$, HK$, NZ$) — but it also drops the legitimate "US$": the "$" is preceded
    // by the letter "S" (excluded), and the standalone "USD" code is absent, so the
    // cell falls through both detection arms and the column is never labelled.
    [Fact(
        Skip = "GH-3118 — CurrencyConsolidationStep does not detect the \"US$\" notation as US Dollars"
    )]
    public void UsDollarPrefixedSymbolColumnFollowedByEmptyColumn_AddsUsDollarsNote()
    {
        var html =
            @"<html><body><table>
  <tr><td>US$</td><td></td><td>100</td></tr>
  <tr><td>US$</td><td></td><td>200</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);

        _sut.Execute(doc);

        var note = doc.QuerySelector("table + p em");
        note.Should().NotBeNull();
        note.TextContent.Should().Be("All values are in US Dollars.");
    }
}
