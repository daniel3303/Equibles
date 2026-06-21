using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsPageValidatorDistinctKeywordTests
{
    // Contract: a page with no IR title phrase qualifies only on at least two DISTINCT content
    // keywords. A single keyword repeated many times is still one distinct hit, so it must NOT
    // qualify — repetition cannot stand in for breadth of IR signal.
    [Fact]
    public void IsInvestorRelationsPage_SingleKeywordRepeatedManyTimes_ReturnsFalse()
    {
        const string html =
            "<html><head><title>Acme Corporation</title></head>"
            + "<body><p>annual report</p><p>annual report</p><p>annual report</p></body></html>";

        InvestorRelationsPageValidator.IsInvestorRelationsPage(html).Should().BeFalse();
    }
}
