using Equibles.Sec.FinancialFacts.BusinessLogic.ReportedStatements;

namespace Equibles.UnitTests.Sec;

public class FilingSummaryParserStatementReportsTests
{
    // A FilingSummary.xml indexes every rendered report; only "Statements"-category entries that
    // carry a rendered R-file are the financial statements we reconstruct. Cover page, notes, and
    // index-only entries (no HtmlFileName) must be excluded, and the survivors kept in filing order.
    private const string FilingSummaryXml = """
        <FilingSummary>
          <MyReports>
            <Report>
              <HtmlFileName>R1.htm</HtmlFileName>
              <LongName>0000001 - Document - Cover Page</LongName>
              <ShortName>Cover Page</ShortName>
              <MenuCategory>Cover</MenuCategory>
              <Position>1</Position>
              <Role>http://acme.com/role/CoverPage</Role>
            </Report>
            <Report>
              <HtmlFileName>R4.htm</HtmlFileName>
              <LongName>0000004 - Statement - CONSOLIDATED BALANCE SHEETS</LongName>
              <ShortName>CONSOLIDATED BALANCE SHEETS</ShortName>
              <MenuCategory>Statements</MenuCategory>
              <Position>4</Position>
              <Role>http://acme.com/role/Balance</Role>
            </Report>
            <Report>
              <HtmlFileName>R2.htm</HtmlFileName>
              <LongName>0000002 - Statement - CONSOLIDATED STATEMENTS OF OPERATIONS</LongName>
              <ShortName>CONSOLIDATED STATEMENTS OF OPERATIONS</ShortName>
              <MenuCategory>Statements</MenuCategory>
              <Position>2</Position>
              <Role>http://acme.com/role/Operations</Role>
            </Report>
            <Report>
              <HtmlFileName>R9.htm</HtmlFileName>
              <ShortName>Summary of Significant Accounting Policies</ShortName>
              <MenuCategory>Notes</MenuCategory>
              <Position>9</Position>
              <Role>http://acme.com/role/Policies</Role>
            </Report>
            <Report>
              <ShortName>Uncategorized statement without a table</ShortName>
              <MenuCategory>Statements</MenuCategory>
              <Position>11</Position>
              <Role>http://acme.com/role/NoTable</Role>
            </Report>
          </MyReports>
        </FilingSummary>
        """;

    [Fact]
    public void StatementReports_KeepsOnlyStatementsWithAnRFile_InPositionOrder()
    {
        var reports = FilingSummaryParser.StatementReports(FilingSummaryXml);

        reports.Select(r => r.HtmlFileName).Should().Equal("R2.htm", "R4.htm");
        reports[0].ShortName.Should().Be("CONSOLIDATED STATEMENTS OF OPERATIONS");
        reports[0].Role.Should().Be("http://acme.com/role/Operations");
    }

    [Fact]
    public void StatementReports_MalformedXml_ReturnsEmpty()
    {
        FilingSummaryParser.StatementReports("<FilingSummary><MyReports>").Should().BeEmpty();
    }

    [Fact]
    public void StatementReports_NullOrBlank_ReturnsEmpty()
    {
        FilingSummaryParser.StatementReports(null).Should().BeEmpty();
        FilingSummaryParser.StatementReports("   ").Should().BeEmpty();
    }
}
