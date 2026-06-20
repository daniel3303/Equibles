using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserParseTransactionImplausibleDateTests
{
    [Fact]
    public void ParseTransactions_TransactionDateAfterFilingDate_AnchorsToPeriodOfReport()
    {
        // Contract: a Form 4 reports an already-executed trade and must be filed within two
        // business days of it, so a transaction can never post-date its filing. A filer year
        // typo (here 2035-01-10, taken from ELDN accession 0000950170-25-004920 whose
        // periodOfReport is the correct 2025-01-10) must not be stored verbatim — the parser
        // anchors it to the filing's period of report rather than letting a year-2035 row sort
        // to the very top of the insider history.
        var root = XElement.Parse(
            """
            <ownershipDocument>
              <nonDerivativeTable>
                <nonDerivativeTransaction>
                  <securityTitle><value>Common Stock</value></securityTitle>
                  <transactionDate><value>2035-01-10</value></transactionDate>
                  <transactionCoding>
                    <transactionCode>A</transactionCode>
                  </transactionCoding>
                  <transactionAmounts>
                    <transactionShares><value>31000</value></transactionShares>
                    <transactionPricePerShare><value>0</value></transactionPricePerShare>
                    <transactionAcquiredDisposedCode><value>A</value></transactionAcquiredDisposedCode>
                  </transactionAmounts>
                </nonDerivativeTransaction>
              </nonDerivativeTable>
            </ownershipDocument>
            """
        );
        var filing = new FilingData
        {
            AccessionNumber = "0000950170-25-004920",
            FilingDate = new DateOnly(2025, 1, 13),
            ReportDate = new DateOnly(2025, 1, 10),
        };

        var result = InsiderFilingParser.ParseTransactions(
            root,
            new InsiderOwner { Id = Guid.NewGuid() },
            Guid.NewGuid(),
            filing,
            isAmendment: false
        );

        var transaction = result.Should().ContainSingle().Subject;
        transaction.TransactionDate.Should().Be(new DateOnly(2025, 1, 10));
    }
}
