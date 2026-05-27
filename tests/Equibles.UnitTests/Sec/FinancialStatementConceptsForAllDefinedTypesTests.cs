using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class FinancialStatementConceptsForAllDefinedTypesTests
{
    // FinancialStatementConcepts is the single source of truth shared by the
    // Web Financials tab and the MCP GetFinancialStatement tool (see XML doc
    // on the class). The docstring promises us-gaap concepts only — a refactor
    // that copy-pasted an IFRS-Full or DEI row into one of the curated lists
    // (FactTaxonomy is a 5-member enum, easy to misclick) would silently feed
    // the wrong taxonomy to SEC's Company Facts lookup, where us-gaap and
    // ifrs-full live under different keys, and every fact would return null.
    // Pin the invariant across every defined statement type so a single bad
    // row fails the build for both consumers.
    [Fact]
    public void For_EveryDefinedStatementType_ReturnsNonEmptyUsGaapOnlyCatalog()
    {
        foreach (var type in Enum.GetValues<FinancialStatementType>())
        {
            var statement = FinancialStatementConcepts.For(type);

            statement
                .Should()
                .NotBeEmpty(
                    $"every defined FinancialStatementType must map to a catalog; {type} returned empty"
                );
            statement
                .Should()
                .OnlyContain(
                    line => line.Taxonomy == FactTaxonomy.UsGaap,
                    $"docstring promises us-gaap concepts only; {type} contains a non-us-gaap row"
                );
        }
    }
}
