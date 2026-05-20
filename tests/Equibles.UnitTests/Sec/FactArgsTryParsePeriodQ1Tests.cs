using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the Q1 switch arm of FactArgs.TryParsePeriod — one arm per PR, sibling
/// to TryParsePeriod_Q4_ReturnsTrueAndQ4 and the lowercase-annual-alias test.
/// A copy-paste regression that mapped "Q1" to Q2 (or to default/FullYear)
/// would compile cleanly and silently misalign every CompareFinancialFact
/// invocation that names a first-quarter period.
/// </summary>
public class FactArgsTryParsePeriodQ1Tests
{
    [Fact]
    public void TryParsePeriod_Q1_ReturnsTrueAndQ1()
    {
        var success = FactArgs.TryParsePeriod("Q1", out var period);

        success.Should().BeTrue();
        period.Should().Be(SecFiscalPeriod.Q1);
    }
}
