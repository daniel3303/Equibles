using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialStatementToolsTryParseStatementPnlAliasTests
{
    // TryParseStatement enumerates SIX aliases that resolve to IncomeStatement:
    //   "income", "income-statement", "incomestatement", "is", "p&l", "pnl".
    // The existing integration test only exercises the canonical "income"; the
    // five remaining aliases — and especially "p&l" with its embedded ampersand —
    // have no per-alias pin. A refactor that "normalizes input by stripping
    // non-alphanumerics" (intuitive to a contributor seeing FinancialConceptAliases.
    // Normalize do exactly that elsewhere in the same module) would silently
    // collapse "p&l" → "pl", which is NOT a registered alias, falling through
    // to the default arm and rejecting the input. Pin "p&l" specifically —
    // it's the alias most exposed to that kind of cleanup.
    [Fact]
    public void TryParseStatement_PnlWithAmpersand_ResolvesToIncomeStatement()
    {
        var method = typeof(FinancialStatementTools).GetMethod(
            "TryParseStatement",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var args = new object[] { "p&l", null };

        var ok = (bool)method.Invoke(null, args);

        ok.Should().BeTrue();
        ((FinancialStatementType)args[1]).Should().Be(FinancialStatementType.IncomeStatement);
    }
}
