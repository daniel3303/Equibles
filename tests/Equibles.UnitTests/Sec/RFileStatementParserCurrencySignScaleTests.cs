using Equibles.Sec.FinancialFacts.BusinessLogic.ReportedStatements;

namespace Equibles.UnitTests.Sec;

// SEC writes one "<sign> in <magnitude>" segment per scaled unit and omits unscaled units, so a
// convenience-translation note such as "$ in Thousands" scales only the dollar column. Titles,
// headers and first values below are copied from the named filings' R-files.
public class RFileStatementParserCurrencySignScaleTests
{
    private static string RFile(string note, string value, params string[] units) =>
        RFileTitled($"Consolidated Statements<br> {note}", value, units);

    private static string RFileTitled(string title, string value, params string[] units)
    {
        var headers = string.Concat(
            units.Select(unit =>
                $"<th class=\"th\"><div>Dec. 31, 2025 </div><div>{unit}</div></th>"
            )
        );
        var cells = string.Concat(units.Select(_ => $"<td class=\"nump\">{value}</td>"));
        return $"""
            <html><body><table class="report">
            <tr><th class="tl" rowspan="2"><div><strong>{title}</strong></div></th><th class="th" colspan="{units.Length}">12 Months Ended</th></tr>
            <tr>{headers}</tr>
            <tr class="re"><td class="pl"><a onclick="Show.showAR( this, 'defref_ifrs-full_ProfitLoss', window );">Profit</a></td>{cells}</tr>
            </table></body></html>
            """;
    }

    private static List<long?> Scales(string html) =>
        RFileStatementParser.Parse(html).Payload.Columns.Select(column => column.Scale).ToList();

    // KB Financial 20-F 2025 (R3): won printed in whole units, the dollar column in thousands.
    [Fact]
    public void Parse_DollarOnlyNote_LeavesAnotherSignsColumnUnscaled()
    {
        var html = RFile(
            "$ in Thousands",
            "5,833,161,872,989",
            "KRW (&#8361;)",
            "USD ($)",
            "KRW (&#8361;)"
        );

        Scales(html).Should().Equal(1L, 1_000L, 1L);
    }

    // KB Financial 20-F 2025 (R2) and HDFC Bank 20-F 2025 (R2): each sign names its own scale.
    [Theory]
    [InlineData("$ in Thousands, &#8361; in Millions", "KRW (&#8361;)", 1_000_000L, 1_000L)]
    [InlineData("&#8360; in Millions, $ in Millions", "INR (&#8360;)", 1_000_000L, 1_000_000L)]
    [InlineData("&#165; in Thousands, $ in Thousands", "CNY (&#165;)", 1_000L, 1_000L)]
    public void Parse_SignedSegments_ScaleTheirOwnColumns(
        string note,
        string homeUnit,
        long homeScale,
        long dollarScale
    )
    {
        Scales(RFile(note, "34,776,877", homeUnit, "USD ($)", homeUnit))
            .Should()
            .Equal(homeScale, dollarScale, homeScale);
    }

    // Chunghwa Telecom 20-F 2025 (R2) prints TWD in units and USD in millions under one "$"
    // segment; TSMC 20-F 2025 (R4) scales both and writes the segment twice.
    [Fact]
    public void Parse_SharedSignWithFewerSegmentsThanCurrencies_LeavesEveryColumnUnresolved()
    {
        Scales(RFile("$ in Millions", "36,944,000,000", "TWD ($)", "USD ($)", "TWD ($)"))
            .Should()
            .Equal(new long?[] { null, null, null });
    }

    [Theory]
    [InlineData("$ in Millions, $ in Millions", "TWD ($)", 1_000_000L)]
    [InlineData("$ in Thousands, $ in Thousands", "CAD ($)", 1_000L)]
    public void Parse_SharedSignWithASegmentPerCurrency_ScalesEveryColumn(
        string note,
        string homeUnit,
        long scale
    )
    {
        Scales(RFile(note, "3,809,054.3", homeUnit, "USD ($)", homeUnit))
            .Should()
            .Equal(scale, scale, scale);
    }

    [Fact]
    public void Parse_SharedSignWithConflictingSegments_LeavesEveryColumnUnresolved()
    {
        Scales(RFile("$ in Millions, $ in Thousands", "1", "CAD ($)", "USD ($)"))
            .Should()
            .Equal(new long?[] { null, null });
    }

    // Pharvaris 6-K 2025 (R5): a euro-only table whose note names a dollar unit used elsewhere.
    [Fact]
    public void Parse_NoteNamingOnlyAnotherSign_LeavesASingleCurrencyTableUnscaled()
    {
        Scales(RFile("$ in Millions", "&#8364; (91,316,060)", "EUR (&#8364;)", "EUR (&#8364;)"))
            .Should()
            .Equal(1L, 1L);
    }

    // Chunghwa Telecom 20-F 2025 (R4) lists its per-share unit once for both dollar signs.
    [Fact]
    public void Parse_SharedSignPerShareUnits_LeavesEveryPerShareColumnUnscaled()
    {
        var columns = RFileStatementParser
            .Parse(RFile("$ / shares in Units, $ in Millions", "3.4", "TWD ($)", "USD ($)"))
            .Payload.Columns;

        columns.Select(column => column.PerShareScale).Should().Equal(1L, 1L);
        columns.Select(column => column.Scale).Should().Equal(new long?[] { null, null });
    }

    [Fact]
    public void Parse_SurplusAgreeingSegments_ScaleTheirColumn()
    {
        Scales(RFile("$ in Thousands, $ in Thousands", "1", "USD ($)")).Should().Equal(1_000L);
    }

    // Letter signs bind like symbols once a column of the table shows them.
    [Theory]
    [InlineData("$ in Thousands", "SEK (kr)", 1L, 1_000L)]
    [InlineData("R in Millions", "ZAR (R)", 1_000_000L, 1L)]
    public void Parse_LetterSigns_ScaleOnlyTheirOwnColumns(
        string note,
        string homeUnit,
        long homeScale,
        long dollarScale
    )
    {
        Scales(RFile(note, "1", homeUnit, "USD ($)")).Should().Equal(homeScale, dollarScale);
    }

    [Fact]
    public void Parse_BracketedTitleToken_IsNotReadAsASign()
    {
        Scales(RFileTitled("CONSOLIDATED BALANCE SHEETS (USD $) In Thousands", "1", "USD ($)"))
            .Should()
            .Equal(1_000L);
    }

    // An equity-statement member such as "Accumulated Net (Loss)" is not an ISO unit and its
    // parenthetical is not a currency sign.
    [Fact]
    public void Parse_MemberParenthetical_IsNotReadAsASign()
    {
        Scales(
                RFile(
                    "USD ($) shares in Thousands, $ in Thousands",
                    "1",
                    "USD ($)",
                    "Accumulated Net (Loss) [Member]"
                )
            )
            .Should()
            .Equal(1_000L, 1_000L);
    }

    [Fact]
    public void Parse_CurrencyPinnedByCode_DoesNotShareTheSignsSegment()
    {
        Scales(RFile("TWD ($) in Millions, $ in Thousands", "1", "TWD ($)", "USD ($)"))
            .Should()
            .Equal(1_000_000L, 1_000L);
    }

    [Fact]
    public void Parse_TitleLabelBeforeAnotherSign_ScalesTheSignedColumn()
    {
        Scales(RFile("KRW (&#8361;) $ in Thousands", "1", "KRW (&#8361;)", "USD ($)"))
            .Should()
            .Equal(1L, 1_000L);
    }
}
