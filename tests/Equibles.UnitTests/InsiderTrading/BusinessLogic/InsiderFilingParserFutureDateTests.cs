using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserFutureDateTests
{
    private static XElement Transaction(string transactionDate) =>
        XElement.Parse(
            $"""
            <nonDerivativeTransaction>
              <securityTitle><value>Common Stock</value></securityTitle>
              <transactionDate><value>{transactionDate}</value></transactionDate>
              <transactionCoding><transactionCode>A</transactionCode></transactionCoding>
              <transactionAmounts>
                <transactionShares><value>100</value></transactionShares>
                <transactionAcquiredDisposedCode><value>A</value></transactionAcquiredDisposedCode>
              </transactionAmounts>
            </nonDerivativeTransaction>
            """
        );

    private static XElement Document(params XElement[] transactions)
    {
        var table = new XElement("nonDerivativeTable", transactions.Cast<object>().ToArray());
        return new XElement("ownershipDocument", table);
    }

    private static List<InsiderTransaction> Parse(XElement root, FilingData filing) =>
        InsiderFilingParser.ParseTransactions(
            root,
            new InsiderOwner { Id = Guid.NewGuid() },
            Guid.NewGuid(),
            filing,
            isAmendment: false
        );

    [Fact]
    public void ParseTransactions_TransactionDateAfterFilingDate_SkipsTransaction()
    {
        // A Form 4 discloses a trade after it occurs, so a transaction date later than the filing
        // date is impossible — a source-typo year (e.g. 2035) must not be stored verbatim.
        var filing = new FilingData
        {
            AccessionNumber = "0000000000-25-000001",
            FilingDate = new DateOnly(2025, 1, 13),
            ReportDate = new DateOnly(2025, 1, 10),
        };

        var result = Parse(Document(Transaction("2035-01-10")), filing);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactions_TransactionDateWithAbsurdPastYear_SkipsTransaction()
    {
        // A two-digit/garbled source year parses to an absurd date (e.g. year 0022) that is well
        // before the filing — outside any sane range and a clear typo, so it must be dropped too.
        var filing = new FilingData
        {
            AccessionNumber = "0000000000-23-000001",
            FilingDate = new DateOnly(2023, 11, 13),
            ReportDate = new DateOnly(2023, 11, 10),
        };

        var result = Parse(Document(Transaction("0022-10-12")), filing);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactions_FutureDatedTransactionAlongsideValidOne_KeepsOnlyValid()
    {
        // The real defect shape: one filing carries a valid row and a typo'd future-dated row
        // (the ELDN/0000950170-25-004920 case). Only the valid row should survive.
        var filing = new FilingData
        {
            AccessionNumber = "0000950170-25-004920",
            FilingDate = new DateOnly(2025, 1, 13),
            ReportDate = new DateOnly(2025, 1, 10),
        };

        var result = Parse(Document(Transaction("2025-01-10"), Transaction("2035-01-10")), filing);

        var kept = result.Should().ContainSingle().Subject;
        kept.TransactionDate.Should().Be(new DateOnly(2025, 1, 10));
    }
}
