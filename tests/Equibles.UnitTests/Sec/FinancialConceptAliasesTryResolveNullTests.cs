using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class FinancialConceptAliasesTryResolveNullTests
{
    [Fact]
    public void TryResolve_NullAlias_ReturnsFalseWithoutThrowing()
    {
        // Contract: TryResolve accepts free-form user input (MCP tool
        // parameters). Null must not throw — it should normalize to empty,
        // find no match, and return false.
        var result = FinancialConceptAliases.TryResolve(null, out var concepts);

        result.Should().BeFalse();
        concepts.Should().BeEmpty();
    }
}
