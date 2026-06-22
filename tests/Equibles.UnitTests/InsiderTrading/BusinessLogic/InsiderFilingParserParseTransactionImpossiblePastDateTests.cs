using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserParseTransactionImpossiblePastDateTests
{
    [Fact]
    public void ParseTransactions_TransactionDateBeforeSaneYearFloor_AnchorsToPeriodOfReport()
    {
        // Contract: a transaction date with an absurd past year (production has rows as early as
        // year 0022, a filer's keyed-wrong value) is impossible and otherwise sorts to the very
        // bottom of a stock's insider history. The parser must reject it and fall back to the
        // filing's period of report, the same anchor used for a date that post-dates its filing.
        var root = XElement.Parse(
            """
            <ownershipDocument>
              <nonDerivativeTable>
                <nonDerivativeTransaction>
                  <securityTitle><value>Common Stock</value></securityTitle>
                  <transactionDate><value>0022-10-12</value></transactionDate>
                  <transactionCoding>
                    <transactionCode>S</transactionCode>
                  </transactionCoding>
                  <transactionAmounts>
                    <transactionShares><value>1000</value></transactionShares>
                    <transactionPricePerShare><value>12.50</value></transactionPricePerShare>
                    <transactionAcquiredDisposedCode><value>D</value></transactionAcquiredDisposedCode>
                  </transactionAmounts>
                </nonDerivativeTransaction>
              </nonDerivativeTable>
            </ownershipDocument>
            """
        );
        var filing = new FilingData
        {
            AccessionNumber = "0000000000-24-000002",
            FilingDate = new DateOnly(2024, 10, 14),
            ReportDate = new DateOnly(2024, 10, 12),
        };

        var result = InsiderFilingParser.ParseTransactions(
            root,
            new InsiderOwner { Id = Guid.NewGuid() },
            Guid.NewGuid(),
            filing,
            isAmendment: false
        );

        var transaction = result.Should().ContainSingle().Subject;
        transaction.TransactionDate.Should().Be(new DateOnly(2024, 10, 12));
    }
}
