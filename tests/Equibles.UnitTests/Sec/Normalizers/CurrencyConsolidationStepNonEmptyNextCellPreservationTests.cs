using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// Sibling to the existing DollarColumnFollowedByEmptyColumn and
/// CurrencyColumnPrecededByLabelColumn pins. Those exercise the homogeneous
/// "every row matches the consolidation pattern" case. The contract that's
/// unpinned — and that SEC filings routinely depend on — is the per-row
/// preservation guarantee: when `ShouldConsolidateColumn` gates on
/// `IsEmptyCell(nextCell)` per row, the processing pass must also honour that
/// gate per row. Header rows with column labels (e.g. "Q1") whose next cell is
/// non-empty must NOT be overwritten by the cleaned currency-column content of
/// a different row.
/// </summary>
public class CurrencyConsolidationStepNonEmptyNextCellPreservationTests
{
    [Fact(
        Skip = "GH-1778 — ProcessCurrencyColumnsForConsolidation does not re-apply the per-row IsEmptyCell(nextCell) gate, so header labels in non-empty next cells are overwritten"
    )]
    public void Execute_HeaderRowWithLabelInNextCell_PreservesLabelDuringConsolidation()
    {
        // Two-row table: row 0 has cells ["", "Q1", ""] — a header with a
        // column label in column 1 — and rows 1-2 have ["$", "", "100"] /
        // ["$", "", "200"] which DO match the (currency, empty-next) gate.
        // Column 0 qualifies for consolidation because rows 1-2 hit the gate;
        // the contract from ShouldConsolidateColumn is that row 0 — which does
        // NOT hit the gate — must be left untouched. Currently the processing
        // pass unconditionally writes cells[1].InnerHtml from cells[0] for
        // every row, destroying the "Q1" label.
        var parser = new HtmlParser();
        var step = new CurrencyConsolidationStep();
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td></td><td>Q1</td><td></td></tr>"
                + "<tr><td>$</td><td></td><td>100</td></tr>"
                + "<tr><td>$</td><td></td><td>200</td></tr>"
                + "</table></body></html>"
        );

        step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        var headerCells = rows[0].QuerySelectorAll("td");
        headerCells
            .Select(c => c.TextContent.Trim())
            .Should()
            .Contain(
                "Q1",
                "the header row's 'Q1' label must survive consolidation — its next cell did not satisfy the per-row gate that ShouldConsolidateColumn enforces"
            );
    }
}
