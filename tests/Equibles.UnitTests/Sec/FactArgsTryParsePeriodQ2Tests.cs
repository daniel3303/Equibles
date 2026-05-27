using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

public class FactArgsTryParsePeriodQ2Tests
{
    // Pins the Q2 switch arm of FactArgs.TryParsePeriod — the last untested
    // quarter discriminator (Q1, Q3, Q4 already have sibling pins). The
    // canonical SEC `fp` value for the second quarter is "Q2"; a copy-paste
    // regression that swapped this case to map "Q2" to Q1 or Q3 would
    // compile cleanly and silently misalign every CompareFinancialFact /
    // GetFinancialFact call that names the second quarter — symptoms would
    // surface only at the fact-comparison level, far from the typo.
    [Fact]
    public void TryParsePeriod_Q2_ReturnsTrueAndQ2()
    {
        var success = FactArgs.TryParsePeriod("Q2", out var period);

        success.Should().BeTrue();
        period.Should().Be(SecFiscalPeriod.Q2);
    }
}
