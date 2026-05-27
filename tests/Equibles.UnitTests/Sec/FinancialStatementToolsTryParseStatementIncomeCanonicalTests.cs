using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialStatementToolsTryParseStatementIncomeCanonicalTests
{
    // Sibling to FinancialStatementToolsTryParseStatementBalanceCanonicalTests
    // (pins canonical "balance" → BalanceSheet) and to the existing "is",
    // "pnl" alias-arm siblings. TryParseStatement's IncomeStatement
    // case-label group has SIX fall-through aliases:
    //   "income", "income-statement", "incomestatement", "is", "p&l", "pnl"
    //
    // Of those, "is" and "pnl" are pinned (short forms). NONE of the
    // four LONG forms is pinned, and "income" is the SINGLE canonical
    // user-facing form per the MCP tool Description that the AI client
    // is told to send.
    //
    // Why "income" specifically warrants its own pin (and the existing
    // siblings cannot catch the regression):
    //
    //   • Drop-the-canonical-label regression — leaves "is" and "pnl"
    //     intact. Passes the "is" pin (different label). Passes the
    //     "pnl" pin (different label). Routes every documented-canonical
    //     `?statement=income` call to the default arm, returning the
    //     self-referentially absurd error message "Unknown statement
    //     'income'" — telling the LLM to use exactly the input it
    //     just sent.
    //
    //   • Swap regression — `"income" => CashFlow` from a copy-paste
    //     touching the wrong arm. Passes every existing pin (they
    //     don't probe "income" specifically). Silently routes every
    //     canonical income-statement query to cash-flow data — a
    //     financial-data MCP tool returning the wrong statement
    //     entirely. The dual assertion (success AND enum value)
    //     catches both axes.
    //
    //   • Bulk-rename drift — a refactor that renamed
    //     `FinancialStatementType.IncomeStatement` → `.IncomeAndLoss`
    //     would touch the switch arm value. The compiler catches the
    //     rename on the enum side, but a copy-paste mistake during
    //     the cleanup that landed the wrong enum value on the
    //     "income" alias's case label would compile cleanly. Pinning
    //     the documented-canonical form catches this at test time.
    //
    // Pin: TryParseStatement("income") returns (true, IncomeStatement).
    // Mirrors the BalanceCanonical sibling's structural shape exactly.
    [Fact]
    public void TryParseStatement_IncomeCanonicalAlias_ResolvesToIncomeStatement()
    {
        var method = typeof(FinancialStatementTools).GetMethod(
            "TryParseStatement",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var args = new object[] { "income", null };

        var ok = (bool)method.Invoke(null, args);

        ok.Should().BeTrue();
        ((FinancialStatementType)args[1]).Should().Be(FinancialStatementType.IncomeStatement);
    }
}
