using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class FundClassifierServicePrecedenceTests
{
    [Fact]
    public void Classify_NameMatchingTwoCategoryPatterns_ReturnsTheEarlierRuleInOrder()
    {
        // Contract: Classify returns the first matching pattern in Rules order. "PENSION"
        // (PensionFund) is deliberately ordered before "INSURANCE" (InsuranceCompany), so a
        // name carrying both must classify as PensionFund. Each existing test matches a single
        // category; this pins the ordered first-match-wins precedence, which a refactor to an
        // unordered dictionary or alphabetized rules would silently break.
        var result = FundClassifierService.Classify("STATE PENSION INSURANCE FUND");

        result.Should().Be(FundClassification.PensionFund);
    }
}
