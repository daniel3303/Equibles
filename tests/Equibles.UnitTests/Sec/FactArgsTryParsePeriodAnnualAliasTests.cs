using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// FactArgs.TryParsePeriod's doc-comment promises "every tool accepts the
/// same spellings" and lists three aliases for the full-year period: FY,
/// FULLYEAR, ANNUAL. The "annual" lowercase form exercises both the alias
/// arm (currently unpinned) and the <c>ToUpperInvariant()</c> tolerance the
/// parser depends on for free-text MCP arguments. A refactor that drops an
/// alias, or swaps <c>ToUpperInvariant()</c> for a culture-sensitive
/// <c>ToUpper()</c>, would compile cleanly and silently reject inputs MCP
/// tool callers are documented to be allowed to send.
/// </summary>
public class FactArgsTryParsePeriodAnnualAliasTests
{
    [Fact]
    public void TryParsePeriod_LowercaseAnnualAlias_ReturnsTrueAndFullYear()
    {
        var success = FactArgs.TryParsePeriod("annual", out var period);

        success.Should().BeTrue();
        period.Should().Be(SecFiscalPeriod.FullYear);
    }
}
