using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Sibling to <see cref="FinancialStatementToolsTryParseStatementPnlAliasTests"/>,
/// <see cref="FinancialStatementToolsTryParseStatementBsAliasTests"/>, and
/// <see cref="FinancialStatementToolsTryParseStatementCfAliasTests"/>. The
/// IncomeStatement arm has six aliases ("income", "income-statement",
/// "incomestatement", "is", "p&amp;l", "pnl"); the PnL pin protects the special
/// ampersand alias and this one protects the two-letter "is" mnemonic. "is" is
/// the most fragile of the six — two characters, looks like a C# keyword in
/// review, and is the most plausible casualty of a "drop the duplicates"
/// simplification pass. Without this pin, removing the case label would silently
/// break every MCP caller that passes <c>statement=is</c>.
/// </summary>
public class FinancialStatementToolsTryParseStatementIsAliasTests
{
    [Fact]
    public void TryParseStatement_IsAlias_ResolvesToIncomeStatement()
    {
        var method = typeof(FinancialStatementTools).GetMethod(
            "TryParseStatement",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { "is", default(FinancialStatementType) };
        var matched = (bool)method.Invoke(null, args);

        matched.Should().BeTrue();
        args[1].Should().Be(FinancialStatementType.IncomeStatement);
    }
}
