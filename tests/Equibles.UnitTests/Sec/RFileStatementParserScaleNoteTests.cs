using Equibles.Sec.FinancialFacts.BusinessLogic.ReportedStatements;

namespace Equibles.UnitTests.Sec;

// The R-file scale note scales each unit family in its own segment — "$ in Thousands",
// "shares in Millions", "$ / shares in Thousands" — and only the money segment may set
// the statement Scale. The production corpus hit every shape below; "USD ($) shares in
// Thousands" filings (dollars UNSCALED, only share counts in thousands) were inflated
// 1000× downstream when any "in Thousands" was read as the money scale.
public class RFileStatementParserScaleNoteTests
{
    // The smallest R-file the parser accepts: a title cell, one dated column and one
    // tagged data row (Parse discards metadata when no data rows survive).
    private static string RFile(string title) =>
        $"""
            <html><body><table class="report">
            <tr><th class="tl">{title}</th><th class="th">Mar. 28, 2026</th></tr>
            <tr class="re"><td class="pl"><a onclick="top.Show.showAR( this, 'defref_us-gaap_Revenues', window );">Revenues</a></td><td class="num">1,234</td></tr>
            </table></body></html>
            """;

    [Theory]
    // A money segment sets the scale, whatever the shares segment says.
    [InlineData("STATEMENT - USD ($) $ in Thousands", 1_000L, "USD")]
    [InlineData("STATEMENT - USD ($) $ in Millions", 1_000_000L, "USD")]
    [InlineData("STATEMENT - USD ($) $ in Billions", 1_000_000_000L, "USD")]
    [InlineData("STATEMENT - USD ($) shares in Thousands, $ in Thousands", 1_000L, "USD")]
    [InlineData("STATEMENT - USD ($) shares in Thousands, $ in Millions", 1_000_000L, "USD")]
    [InlineData("STATEMENT - USD ($) $ in Thousands, shares in Millions", 1_000L, "USD")]
    [InlineData("STATEMENT - USD ($) $ / shares in Thousands, $ in Thousands", 1_000L, "USD")]
    // Shares-only and per-share-only scales leave money unscaled.
    [InlineData("STATEMENT - USD ($) shares in Thousands", 1L, "USD")]
    [InlineData("STATEMENT - USD ($) shares in Millions", 1L, "USD")]
    [InlineData("STATEMENT - USD ($) $ / shares in Thousands", 1L, "USD")]
    [InlineData("STATEMENT - $ / shares shares in Thousands", 1L, null)]
    [InlineData("STATEMENT - shares shares in Millions", 1L, null)]
    // No scale segments at all.
    [InlineData("STATEMENT - USD ($)", 1L, "USD")]
    [InlineData("STATEMENT - Futures Contracts", 1L, null)]
    // Foreign reporting currencies carry their ISO code before the symbol.
    [InlineData("STATEMENT - EUR (€) € in Thousands", 1_000L, "EUR")]
    [InlineData("STATEMENT - CAD ($) $ in Millions", 1_000_000L, "CAD")]
    [InlineData("STATEMENT - BRL (R$) R$ in Thousands", 1_000L, "BRL")]
    // Old-style titles without a " - " note: a subject-less "(In Thousands)" is money.
    [InlineData("CONSOLIDATED BALANCE SHEETS (In Thousands, except per share data)", 1_000L, null)]
    [InlineData(
        "STATEMENTS OF OPERATIONS (In Millions, except share and per share amounts)",
        1_000_000L,
        null
    )]
    public void Parse_ScaleNote_YieldsMoneyScaleAndCurrency(
        string title,
        long expectedScale,
        string expectedCurrency
    )
    {
        var statement = RFileStatementParser.Parse(RFile(title));

        statement.IsEmpty.Should().BeFalse();
        statement.Scale.Should().Be(expectedScale);
        statement.Currency.Should().Be(expectedCurrency);
    }
}
