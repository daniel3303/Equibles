using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// FactArgs.TryParsePeriod is the shared parser every FinancialFacts MCP tool
/// uses to interpret user-supplied period arguments. The Q4 branch is the
/// canonical SEC <c>fp</c> value for the fourth quarter (per the SEC Company
/// Facts API), uses no aliases, and is currently unpinned — every other
/// switch arm (FY/FullYear/Annual, Q1, Q2, Q3) is also uncovered, but
/// one-test-per-PR. A refactor that swaps the Q4 case to <c>Q3</c> (an easy
/// copy-paste slip) would compile cleanly and break time-series tools that
/// rely on this discriminator to align facts to the right period.
/// </summary>
public class FactArgsTryParsePeriodTests
{
    [Fact]
    public void TryParsePeriod_Q4_ReturnsTrueAndQ4()
    {
        var success = FactArgs.TryParsePeriod("Q4", out var period);

        success.Should().BeTrue();
        period.Should().Be(SecFiscalPeriod.Q4);
    }
}
