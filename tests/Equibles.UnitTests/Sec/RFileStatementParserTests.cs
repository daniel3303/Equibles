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
        statement.PrimaryPeriodEnd.Should().Be(new DateOnly(2026, 3, 28));

        var payload = statement.Payload;
        // 3 Months Ended (current + prior) + 6 Months Ended (current + prior).
        payload.Columns.Should().HaveCount(4);
        payload.Columns[0].Label.Should().Be("Mar. 28, 2026");
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
    public void Parse_NoTable_ReturnsEmpty()
    {
        RFileStatementParser
            .Parse("<html><body>no statement here</body></html>")
            .IsEmpty.Should()
            .BeTrue();
        RFileStatementParser.Parse(null).IsEmpty.Should().BeTrue();
    }
}
