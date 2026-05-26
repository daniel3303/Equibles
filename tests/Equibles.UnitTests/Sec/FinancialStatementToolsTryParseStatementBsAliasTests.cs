using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialStatementToolsTryParseStatementBsAliasTests
{
    // Sibling to FinancialStatementToolsTryParseStatementPnlAliasTests. The
    // TryParseStatement cascade has FOUR aliases for BalanceSheet: "balance",
    // "balance-sheet", "balancesheet", "bs". The IntegrationTests file only
    // calls GetFinancialStatement with statement="income" / "wat", so the
    // entire BalanceSheet arm is currently unhit by the suite. A refactor
    // that consolidated the alias list and quietly dropped the 2-character
    // "bs" (the shortest, most prone to "looks like a typo" cleanup) would
    // route every MCP call requesting `?statement=bs` to the default arm
    // and surface "Unknown statement 'bs'" to the AI client. Pin the alias.
    [Fact]
    public void TryParseStatement_BsAlias_ResolvesToBalanceSheet()
    {
        var method = typeof(FinancialStatementTools).GetMethod(
            "TryParseStatement",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var args = new object[] { "bs", null };

        var ok = (bool)method.Invoke(null, args);

        ok.Should().BeTrue();
        ((FinancialStatementType)args[1]).Should().Be(FinancialStatementType.BalanceSheet);
    }
}
