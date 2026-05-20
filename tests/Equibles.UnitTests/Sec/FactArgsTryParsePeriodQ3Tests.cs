using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the Q3 switch arm of FactArgs.TryParsePeriod — sibling to the existing
/// Q1 (#1270) and Q4 pins, one arm per PR. A copy-paste regression mapping
/// "Q3" to Q2 or Q4 would compile cleanly and silently misalign every
/// CompareFinancialFact invocation that names a third-quarter period.
/// </summary>
public class FactArgsTryParsePeriodQ3Tests
{
    [Fact]
    public void TryParsePeriod_Q3_ReturnsTrueAndQ3()
    {
        var success = FactArgs.TryParsePeriod("Q3", out var period);

        success.Should().BeTrue();
        period.Should().Be(SecFiscalPeriod.Q3);
    }
}
