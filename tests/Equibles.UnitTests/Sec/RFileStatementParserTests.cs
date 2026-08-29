using Equibles.Sec.FinancialFacts.BusinessLogic.ReportedStatements;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

// Record-replay: the fixtures are real R-files captured from Apple's FY2026 Q2 10-Q
// (accession 0000320193-26-000013), exactly as the capture step stores them (SGML-wrapped).
// Frozen input → exact-value assertions; a diff means the parser regressed.
public class RFileStatementParserTests
{
    private static string Load(string file) =>
        File.ReadAllText(Path.Combine("TestAssets", "ReportedStatements", file));

    private static ReportedStatementRow Row(ReportedStatementPayload p, string label) =>
        p.Rows.First(r => r.Label == label);

    [Fact]
    public void Parse_IncomeStatement_ReadsColumnsConceptsValuesAndStructure()
    {
        var statement = RFileStatementParser.Parse(Load("aapl-10q-income-R2.htm"));

        statement.IsEmpty.Should().BeFalse();
        statement.Currency.Should().Be("USD");
        statement.Scale.Should().Be(1_000_000);
        statement.PrimaryIsInstant.Should().BeFalse();
        statement.PrimaryPeriodStart.Should().Be(new DateOnly(2025, 12, 29));
        statement.PrimaryPeriodEnd.Should().Be(new DateOnly(2026, 3, 28));

        var payload = statement.Payload;
        // 3 Months Ended (current + prior) + 6 Months Ended (current + prior).
        payload.Columns.Should().HaveCount(4);
        payload.Columns[0].Label.Should().Be("Mar. 28, 2026");
        payload.Columns[0].PeriodEnd.Should().Be(new DateOnly(2026, 3, 28));
        payload.Columns[0].Currency.Should().Be("USD");
        payload.Columns[0].Scale.Should().Be(1_000_000L);
        payload.Columns[0].PerShareScale.Should().Be(1L);
        payload.Columns[0].Duration.Should().Be("3 Months Ended");
        payload.Columns[0].IsInstant.Should().BeFalse();

        // Top line carries its us-gaap concept and the as-filed value (in millions).
        var netSales = Row(payload, "Net sales");
        netSales.Taxonomy.Should().Be("us-gaap");
        netSales.Concept.Should().Be("RevenueFromContractWithCustomerExcludingAssessedTax");
        netSales.Values[0].Should().Be(111_184m);

        var operatingExpenses = Row(payload, "Operating expenses:");
        operatingExpenses.IsAbstract.Should().BeTrue();
        operatingExpenses.Depth.Should().Be(0);

        var rnd = Row(payload, "Research and development");
        rnd.IsAbstract.Should().BeFalse();
        rnd.Depth.Should().Be(1); // indented under "Operating expenses:"
        rnd.Values[0].Should().Be(11_419m);
    }

    [Fact]
    public void Parse_MultiCurrencyColumnHeaders_RetainDateAndExactCurrency()
    {
        const string html = """
            <html><body><table class="report">
            <tr>
              <th class="tl" rowspan="2">CONSOLIDATED STATEMENTS - $ in Millions</th>
              <th class="th" colspan="3">12 Months Ended</th>
            </tr>
            <tr>
              <th class="th"><div>Dec. 31, 2025</div><div>TWD ($)</div><div>$ / shares</div></th>
              <th class="th"><div>Dec. 31, 2025</div><div>USD ($)</div><div>$ / shares</div></th>
              <th class="th"><div>Dec. 31, 2024</div><div>TWD ($)</div><div>$ / shares</div></th>
            </tr>
            <tr class="re"><td class="pl"><a onclick="top.Show.showAR( this, 'defref_ifrs-full_RevenueFromContractsWithCustomers', window );">Revenue</a></td><td class="num">3,809,054.3</td><td class="num">121,423.5</td><td class="num">2,894,307.7</td></tr>
            </table></body></html>
            """;

        var statement = RFileStatementParser.Parse(html);

        statement.IsEmpty.Should().BeFalse();
        statement.Currency.Should().Be("TWD");
        statement.PrimaryPeriodStart.Should().Be(new DateOnly(2025, 1, 1));
        statement.PrimaryPeriodEnd.Should().Be(new DateOnly(2025, 12, 31));
        statement
            .Payload.Columns.Select(column => column.Label)
            .Should()
            .Equal("Dec. 31, 2025", "Dec. 31, 2025", "Dec. 31, 2024");
        statement
            .Payload.Columns.Select(column => column.Currency)
            .Should()
            .Equal("TWD", "USD", "TWD");
        statement
            .Payload.Columns.Select(column => column.PeriodEnd)
            .Should()
            .Equal(
                new DateOnly(2025, 12, 31),
                new DateOnly(2025, 12, 31),
                new DateOnly(2024, 12, 31)
            );
        statement
            .Payload.Columns.Select(column => column.Scale)
            .Should()
            .Equal(1_000_000L, 1_000_000L, 1_000_000L);
    }

    [Fact]
    public void Parse_MultiCurrencyTitle_RetainsExactScaleForEachColumn()
    {
        const string html = """
            <html><body><table class="report">
            <tr>
              <th class="tl" rowspan="2">CONSOLIDATED STATEMENTS - TWD ($) in Millions, USD ($) in Thousands</th>
              <th class="th" colspan="2">12 Months Ended</th>
            </tr>
            <tr>
              <th class="th">Dec. 31, 2025 TWD ($)</th>
              <th class="th">Dec. 31, 2025 USD ($)</th>
            </tr>
            <tr class="re"><td class="pl"><a onclick="top.Show.showAR( this, 'defref_ifrs-full_Revenue', window );">Revenue</a></td><td class="num">100</td><td class="num">4</td></tr>
            </table></body></html>
            """;

        var columns = RFileStatementParser.Parse(html).Payload.Columns;

        columns.Select(column => column.Currency).Should().Equal("TWD", "USD");
        columns.Select(column => column.Scale).Should().Equal(1_000_000L, 1_000L);
    }

    [Fact]
    public void Parse_MultiCurrencyTitleWithDateOnlyColumn_FailsClosed()
    {
        const string html = """
            <html><body><table class="report">
            <tr>
              <th class="tl" rowspan="2">CONSOLIDATED STATEMENTS - TWD ($) in Millions, USD ($) in Thousands</th>
              <th class="th">12 Months Ended</th>
            </tr>
            <tr><th class="th">Dec. 31, 2025</th></tr>
            <tr class="re"><td class="pl"><a onclick="top.Show.showAR( this, 'defref_ifrs-full_Revenue', window );">Revenue</a></td><td class="num">100</td></tr>
            </table></body></html>
            """;

        var column = RFileStatementParser.Parse(html).Payload.Columns.Single();

        column.Currency.Should().BeNull();
        column.Scale.Should().BeNull();
        column.PerShareScale.Should().BeNull();
    }

    [Fact]
    public void Parse_ConflictingSameCurrencyScales_LeavesColumnScaleAmbiguous()
    {
        const string html = """
            <html><body><table class="report">
            <tr>
              <th class="tl" rowspan="2">CONSOLIDATED STATEMENTS - TWD ($) in Millions, TWD ($) in Thousands</th>
              <th class="th">12 Months Ended</th>
            </tr>
            <tr><th class="th">Dec. 31, 2025 TWD ($)</th></tr>
            <tr class="re"><td class="pl"><a onclick="top.Show.showAR( this, 'defref_ifrs-full_Revenue', window );">Revenue</a></td><td class="num">100</td></tr>
            </table></body></html>
            """;

        RFileStatementParser.Parse(html).Payload.Columns.Single().Scale.Should().BeNull();
    }

    [Fact]
    public void Parse_ExplicitScalesForOtherCurrencies_DoNotAuthorizeColumnScale()
    {
        const string html = """
            <html><body><table class="report">
            <tr>
              <th class="tl" rowspan="2">CONSOLIDATED STATEMENTS - TWD ($) in Millions, USD ($) in Millions</th>
              <th class="th">12 Months Ended</th>
            </tr>
            <tr><th class="th">Dec. 31, 2025 EUR (€)</th></tr>
            <tr class="re"><td class="pl"><a onclick="top.Show.showAR( this, 'defref_ifrs-full_Revenue', window );">Revenue</a></td><td class="num">100</td></tr>
            </table></body></html>
            """;

        RFileStatementParser.Parse(html).Payload.Columns.Single().Scale.Should().BeNull();
    }

    [Fact]
    public void Parse_MultiCurrencyPerShareClauses_RetainExactColumnScales()
    {
        const string html = """
            <html><body><table class="report">
            <tr>
              <th class="tl" rowspan="2">INCOME - TWD ($) TWD / shares in Units, USD ($) USD / shares in Thousands, $ in Millions</th>
              <th class="th" colspan="2">12 Months Ended</th>
            </tr>
            <tr><th class="th">Dec. 31, 2025 TWD ($)</th><th class="th">Dec. 31, 2025 USD ($)</th></tr>
            <tr class="re"><td class="pl"><a onclick="top.Show.showAR( this, 'defref_ifrs-full_DilutedEarningsLossPerShare', window );">EPS</a></td><td class="num">65.47</td><td class="num">0.002</td></tr>
            </table></body></html>
            """;

        var columns = RFileStatementParser.Parse(html).Payload.Columns;

        columns.Select(column => column.PerShareScale).Should().Equal(1L, 1_000L);
    }

    [Fact]
    public void Parse_WeekBasedForeignStatement_RetainsDurationAndFiscalAnchor()
    {
        const string html = """
            <html><body><table class="report">
            <tr>
              <th class="tl" rowspan="2">CONSOLIDATED STATEMENTS - TWD ($) in Millions</th>
              <th class="th">52 Weeks Ended</th>
            </tr>
            <tr><th class="th">Sept. 30, 2025 TWD ($)</th></tr>
            <tr class="re"><td class="pl"><a onclick="top.Show.showAR( this, 'defref_ifrs-full_Revenue', window );">Revenue</a></td><td class="num">100</td></tr>
            </table></body></html>
            """;

        var statement = RFileStatementParser.Parse(html);

        statement.PrimaryIsInstant.Should().BeFalse();
        statement.PrimaryPeriodStart.Should().Be(new DateOnly(2024, 10, 2));
        statement.PrimaryPeriodEnd.Should().Be(new DateOnly(2025, 9, 30));
        statement.Payload.Columns.Single().Label.Should().Be("Sept. 30, 2025");
        statement.Payload.Columns.Single().PeriodEnd.Should().Be(new DateOnly(2025, 9, 30));
    }

    [Fact]
    public void Parse_BalanceSheet_IsInstantAndMarksTotals()
    {
        var statement = RFileStatementParser.Parse(Load("aapl-10q-balance-R4.htm"));

        statement.IsEmpty.Should().BeFalse();
        statement.PrimaryIsInstant.Should().BeTrue();
        statement.PrimaryPeriodEnd.Should().Be(new DateOnly(2026, 3, 28));

        var payload = statement.Payload;
        payload.Columns.Should().HaveCount(2);
        payload.Columns.Should().OnlyContain(c => c.IsInstant);
        payload.Columns.Select(c => c.Label).Should().Equal("Mar. 28, 2026", "Sep. 27, 2025");

        // "Total assets" is rendered as a total row (us-gaap:Assets).
        var totalAssets = payload.Rows.First(r => r.Concept == "Assets" && r.Taxonomy == "us-gaap");
        totalAssets.IsTotal.Should().BeTrue();
        totalAssets.Values[0].Should().NotBeNull();
    }

    [Fact]
    public void Parse_PresentationMemberHeader_IsInheritedByOnlyItsFollowingRows()
    {
        const string html = """
            <html><body><table class="report">
            <tr>
              <th class="tl" rowspan="2">INCOME - TWD ($) $ / shares in Units</th>
              <th class="th">12 Months Ended</th>
            </tr>
            <tr><th class="th">Dec. 31, 2025 TWD ($)</th></tr>
            <tr class="ro"><td class="pl"><a onclick="top.Show.showAR( this, 'defref_ifrs-full_DilutedEarningsLossPerShare', window );">Diluted EPS</a></td><td class="num">65.47</td></tr>
            <tr class="rh"><td class="pl"><a onclick="top.Show.showAR( this, 'defref_ifrs-full_ClassesOfShareCapitalAxis=tsm_AmericanDepositarySharesMember', window );">American depositary shares</a></td><td class="text">&#160;</td></tr>
            <tr class="ro"><td class="pl"><a onclick="top.Show.showAR( this, 'defref_ifrs-full_DilutedEarningsLossPerShare', window );">Diluted EPS</a></td><td class="num">327.34</td></tr>
            </table></body></html>
            """;

        var rows = RFileStatementParser.Parse(html).Payload.Rows;

        rows.Should().HaveCount(2);
        rows[0].PresentationContext.Should().BeNull();
        rows[1]
            .PresentationContext.Should()
            .Be("defref_ifrs-full_ClassesOfShareCapitalAxis=tsm_AmericanDepositarySharesMember");
    }

    [Fact]
    public void Parse_NoTable_ReturnsEmpty()
    {
        RFileStatementParser
            .Parse("<html><body>no statement here</body></html>")
            .IsEmpty.Should()
            .BeTrue();
        RFileStatementParser.Parse(null).IsEmpty.Should().BeTrue();
    }
}
