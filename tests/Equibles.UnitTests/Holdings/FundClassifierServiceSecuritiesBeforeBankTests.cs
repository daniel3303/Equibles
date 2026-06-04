using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class FundClassifierServiceSecuritiesBeforeBankTests
{
    // Contract: Classify returns the first matching pattern in Rules order.
    // "SECURITIES" (BrokerDealer) is ordered before "BANK " (Bank), so a bank's
    // broker-dealer subsidiary — a common 13F filer — must classify by its
    // function (BrokerDealer), not by the trailing "BANK" token. The existing
    // precedence pin covers PENSION-before-INSURANCE; this pins a different,
    // real-world rule pair that an unordered/alphabetized refactor would break.
    [Fact]
    public void Classify_NameWithBothSecuritiesAndBank_ReturnsBrokerDealer()
    {
        var result = FundClassifierService.Classify("DEUTSCHE BANK SECURITIES INC");

        result.Should().Be(FundClassification.BrokerDealer);
    }
}
