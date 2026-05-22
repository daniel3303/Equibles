using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// The existing TableWithoutCurrencySymbols pin asserts that a table whose
/// cells contain no currency tokens at all is left unchanged. The unpinned
/// contract is the substring-vs-word-boundary one: a cell whose text merely
/// EMBEDS a currency ISO code as a substring of an unrelated acronym
/// ("USDA", "USDC", "EUREKA") must not be treated as a currency cell.
/// DetectCurrency's `text.Contains(code)` branch matches the substring
/// anywhere, so a perfectly legitimate label like "USDA inspected facilities"
/// gets nominated for consolidation, its content stripped through
/// RemoveCurrencySymbols (which also uses Replace globally), and a misleading
/// "All values are in US Dollars." note is appended below an otherwise
/// non-currency table.
/// </summary>
public class CurrencyConsolidationStepCurrencyCodeSubstringFalsePositiveTests
{
    [Fact(
        Skip = "GH-1795 — DetectCurrency's text.Contains(code) branch substring-matches ISO codes inside unrelated acronyms (USDA/USDC/EUREKA), shredding labels and appending a misleading currency note"
    )]
    public void Execute_LabelEmbedsCurrencyCodeAsSubstring_LeavesTableUnchanged()
    {
        // "USDA inspected facilities" embeds "USD" inside the acronym "USDA"
        // — no actual dollar sign, no standalone USD token. The contract:
        // such a cell is NOT a currency cell and the column must not be
        // consolidated. DetectCurrency currently returns "USD" on any
        // substring hit, so the label is shredded to "A inspected facilities"
        // and a misleading US Dollars note is appended.
        var parser = new HtmlParser();
        var step = new CurrencyConsolidationStep();
        var html =
            @"<html><body><table>
  <tr><td>USDA inspected facilities</td><td></td><td>100</td></tr>
</table></body></html>";
        var doc = parser.ParseDocument(html);
        var originalHtml = doc.DocumentElement.OuterHtml;

        step.Execute(doc);

        doc.DocumentElement.OuterHtml.Should()
            .Be(
                originalHtml,
                "no cell contains an actual currency symbol or standalone currency code — the embedded 'USD' inside the acronym 'USDA' must not trigger consolidation"
            );
    }
}
