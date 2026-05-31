using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorSecurityKindTests
{
    // InsiderFilingParser.ParseTransactions walks the two Form 4 tables. The
    // table a row sits in is the AUTHORITATIVE security classification — the
    // non-derivative table holds the issuer's actual shares, the derivative
    // table holds options/warrants/convertibles. This pins that each row is
    // tagged from its source table rather than from the (unreliable) title
    // text, and that every parsed row is stamped with the current parser
    // version so the reprocessing pipeline can find stale rows.
    private static List<InsiderTransaction> ParseAll(XElement root)
    {
        var owner = new InsiderOwner { OwnerCik = "0000000001", Name = "Test Owner" };
        var filing = new FilingData
        {
            AccessionNumber = "0000000000-26-000001",
            Form = "4",
            FilingDate = new DateOnly(2026, 1, 5),
            ReportDate = new DateOnly(2026, 1, 2),
        };

        return InsiderFilingParser.ParseTransactions(root, owner, Guid.NewGuid(), filing, false);
    }

    private static XElement Transaction(string securityTitle) =>
        new(
            "nonDerivativeTransaction",
            new XElement("securityTitle", new XElement("value", securityTitle)),
            new XElement("transactionDate", new XElement("value", "2026-01-02")),
            new XElement("transactionCoding", new XElement("transactionCode", "P")),
            new XElement(
                "transactionAmounts",
                new XElement("transactionShares", new XElement("value", "1000")),
                new XElement("transactionPricePerShare", new XElement("value", "10"))
            )
        );

    private static XElement DerivativeTransaction(string securityTitle) =>
        new XElement(Transaction(securityTitle)) { Name = "derivativeTransaction" };

    [Fact]
    public void ParseAllTransactions_TagsRowsFromTheirSourceTable_NotTheTitleText()
    {
        // A "Common Stock" row that lives in the derivative table (e.g. the
        // common-stock underlying a converted note) must classify as Derivative
        // from its table, and a deliberately option-titled row in the
        // non-derivative table must classify as NonDerivative — proving the
        // classification follows the table, never the title keywords.
        var root = new XElement(
            "ownershipDocument",
            new XElement("nonDerivativeTable", Transaction("Common Stock")),
            new XElement("derivativeTable", DerivativeTransaction("Common Stock"))
        );

        var transactions = ParseAll(root);

        transactions.Should().HaveCount(2);
        transactions[0].SecurityKind.Should().Be(InsiderSecurityKind.NonDerivative);
        transactions[1].SecurityKind.Should().Be(InsiderSecurityKind.Derivative);
    }

    [Fact]
    public void ParseAllTransactions_StampsEveryRowWithCurrentParserVersion()
    {
        var root = new XElement(
            "ownershipDocument",
            new XElement("nonDerivativeTable", Transaction("Common Stock")),
            new XElement("derivativeTable", DerivativeTransaction("Stock Option (Right to Buy)"))
        );

        var transactions = ParseAll(root);

        transactions
            .Should()
            .OnlyContain(t => t.ParserVersion == InsiderTransaction.CurrentParserVersion);
    }
}
