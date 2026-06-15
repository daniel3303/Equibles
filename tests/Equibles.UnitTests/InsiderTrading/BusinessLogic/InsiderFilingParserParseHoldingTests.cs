using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserParseHoldingTests
{
    [Fact]
    public void ParseTransactions_NonDerivativeHolding_ProducesHoldingSnapshotRow()
    {
        // Contract: a Form 3/4/5 *holding* (a position reported with no transaction) parses into an
        // InsiderTransaction snapshot — TransactionCode.Holding (a position, not a trade), no price,
        // Acquired, and Shares == SharesOwnedAfter == the post-transaction holding amount. The
        // Holding tag is what lets transaction lists drop these while ownership summaries keep them.
        var root = XElement.Parse(
            """
            <ownershipDocument>
              <nonDerivativeTable>
                <nonDerivativeHolding>
                  <securityTitle><value>Common Stock</value></securityTitle>
                  <postTransactionAmounts>
                    <sharesOwnedFollowingTransaction><value>5000</value></sharesOwnedFollowingTransaction>
                  </postTransactionAmounts>
                </nonDerivativeHolding>
              </nonDerivativeTable>
            </ownershipDocument>
            """
        );
        var filing = new FilingData
        {
            AccessionNumber = "0000000000-24-000001",
            FilingDate = new DateOnly(2024, 2, 1),
            ReportDate = new DateOnly(2024, 1, 31),
        };

        var result = InsiderFilingParser.ParseTransactions(
            root,
            new InsiderOwner { Id = Guid.NewGuid() },
            Guid.NewGuid(),
            filing,
            isAmendment: false
        );

        var holding = result.Should().ContainSingle().Subject;
        holding.TransactionCode.Should().Be(TransactionCode.Holding);
        holding.PricePerShare.Should().Be(0);
        holding.AcquiredDisposed.Should().Be(AcquiredDisposed.Acquired);
        holding.Shares.Should().Be(5000);
        holding.SharesOwnedAfter.Should().Be(5000);
        holding.SecurityKind.Should().Be(InsiderSecurityKind.NonDerivative);
    }
}
