using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class CurrencyConsolidationStepRaggedRowTests
{
    // Contract: SEC filing HTML is routinely ragged — rows with fewer cells than
    // the widest row are common. Currency consolidation scans by column index, so
    // it must bounds-check each row and skip ones too short for the column being
    // examined, never index past the row's cells. A well-formed currency column
    // must still be detected and a note added; the short row must not crash it.
    // Without the bounds guard, indexing the short row throws IndexOutOfRange.
    [Fact]
    public void Execute_TableHasRowShorterThanCurrencyColumn_ConsolidatesWithoutThrowing()
    {
        var parser = new HtmlParser();
        var step = new CurrencyConsolidationStep();
        // Rows 1 & 3 are a currency column (USD, empty next cell); row 2 is ragged
        // (a single cell) — far shorter than the USD column index path.
        var html =
            @"<html><body><table>
  <tr><td>USD</td><td></td><td>100</td></tr>
  <tr><td>subtotal</td></tr>
  <tr><td>USD</td><td></td><td>200</td></tr>
</table></body></html>";
        var doc = parser.ParseDocument(html);

        step.Execute(doc);

        // Detection survived the ragged row: the US Dollars note was appended.
        doc.DocumentElement.OuterHtml.Should().Contain("All values are in US Dollars.");
    }
}
