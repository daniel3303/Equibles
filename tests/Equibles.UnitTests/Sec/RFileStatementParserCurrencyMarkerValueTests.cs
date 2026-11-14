using Equibles.Sec.FinancialFacts.BusinessLogic.ReportedStatements;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

// SEC prints the currency before the figure on a statement's first and total rows. Cells below
// are copied from R-files: a 2025 10-K's net loss and 20-F rows in euros, Swiss francs and reais.
public class RFileStatementParserCurrencyMarkerValueTests
{
    private static string RFile(string cellClass, params string[] cells) =>
        $"""
            <html><body><table class="report">
            <tr><th class="tl" rowspan="2"><div><strong>Consolidated Statements of Operations</strong></div></th><th class="th" colspan="{cells.Length}">12 Months Ended</th></tr>
            <tr>{string.Concat(
                cells.Select((_, index) => $"<th class=\"th\"><div>Dec. 31, {2025 - index}</div></th>")
            )}</tr>
            <tr class="reu"><td class="pl"><a onclick="Show.showAR( this, 'defref_us-gaap_NetIncomeLoss', window );">Net Loss</a></td>{string.Concat(
                cells.Select(cell => $"<td class=\"{cellClass}\">{cell}<span></span> </td>")
            )}</tr>
            </table></body></html>
            """;

    private static ReportedStatementRow OnlyRow(string html) =>
        RFileStatementParser.Parse(html).Payload.Rows.Should().ContainSingle().Subject;

    [Fact]
    public void Parse_DollarSignBeforeParentheses_ReadsTheLossAsNegative()
    {
        var row = OnlyRow(RFile("num", "$ (24,761,369)", "$ (21,148,747)"));

        row.Values.Should().Equal(-24_761_369m, -21_148_747m);
    }

    [Theory]
    [InlineData("&#8364; 47,183", 47_183)]
    [InlineData("&#8364; (91,316,060)", -91_316_060)]
    [InlineData("&#163; 29,324", 29_324)]
    [InlineData("&#165; 3,316,238", 3_316_238)]
    [InlineData("&#8361; 2,669,885", 2_669_885)]
    [InlineData("SFr 1,638,612", 1_638_612)]
    [InlineData("R$ 16,046,218", 16_046_218)]
    [InlineData("kr 11,583,000", 11_583_000)]
    [InlineData("$ 0.35", 0.35)]
    public void Parse_AnyCurrencyMarker_KeepsTheFigureAndTheRowIsNotAHeading(
        string cell,
        decimal expected
    )
    {
        var row = OnlyRow(RFile(expected < 0 ? "num" : "nump", cell));

        row.Values.Should().Equal(expected);
        row.IsAbstract.Should().BeFalse();
    }

    // Words in a numeric cell are not a currency marker: the cell stays without a figure.
    [Theory]
    [InlineData("Not applicable 5")]
    [InlineData("1 year 6 months")]
    public void Parse_TextInANumericCell_StaysWithoutAFigure(string cell)
    {
        OnlyRow(RFile("nump", cell, "12")).Values.Should().Equal(null, 12m);
    }
}
