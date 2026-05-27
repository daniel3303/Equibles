using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class FinancialStatementConceptsForUndefinedTypeTests
{
    // Sibling to FinancialStatementConceptsForAllDefinedTypesTests (which pins
    // the three defined arms). The switch's default `_ => []` arm fires when
    // the caller supplies an out-of-enum value — possible via a stale int
    // round-tripped from disk, a future enum-member rename, or a hand-crafted
    // query-string. The contract is "no catalog → empty list" so downstream
    // renderers display nothing (rather than throwing or falling back to a
    // stale IncomeStatement default). A refactor that replaced `_ => []` with
    // `_ => IncomeStatement` (or `throw`) would silently mis-render every
    // request for a future fourth statement type. Pin the empty fallback.
    [Fact]
    public void For_UndefinedFinancialStatementType_ReturnsEmptyList()
    {
        var result = FinancialStatementConcepts.For((FinancialStatementType)999);

        result.Should().BeEmpty();
    }
}
