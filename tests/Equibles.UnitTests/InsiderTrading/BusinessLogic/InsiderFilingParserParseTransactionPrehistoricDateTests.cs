using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

/// <summary>
/// Sibling to <see cref="InsiderFilingParserParseTransactionImplausibleDateTests"/>, which pins the
/// upper bracket of the plausibility window (a date AFTER the filing date is anchored to the period
/// of report). This pins the opposite, equally-promised lower bracket: <c>IsPlausibleTransactionDate</c>
/// also floors the year at <c>MinPlausibleTransactionYear</c> (1900) so a keyed-wrong far-past year
/// — the earliest seen in production was <c>0022</c> — is treated as implausible and anchored to the
/// period of report rather than stored verbatim and sorted to the very bottom of the insider history.
/// </summary>
public class InsiderFilingParserParseTransactionPrehistoricDateTests
{
    [Fact]
    public void ParseTransactions_TransactionDateYearBeforeFloor_AnchorsToPeriodOfReport()
    {
        // Contract: the plausibility window brackets the transaction date in BOTH directions. A year
        // below the 1900 floor (here 0022-01-10, the keyed-wrong far-past style the doc cites as the
        // earliest seen in production) is a filer typo, not a real trade, so the parser must anchor it
        // to the filing's period of report — NOT store the year-0022 date, which would sort to the
        // very bottom of the insider history. A regression that floored only the upper bound (date
        // after filing) would leave the 0022 date verbatim and fail here.
        var root = XElement.Parse(
            """
            <ownershipDocument>
              <nonDerivativeTable>
                <nonDerivativeTransaction>
                  <securityTitle><value>Common Stock</value></securityTitle>
                  <transactionDate><value>0022-01-10</value></transactionDate>
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
