using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialStatementToolsTryParseStatementCfAliasTests
{
    // Sibling to FinancialStatementToolsTryParseStatementBsAliasTests (PR #2199)
    // and the PnL alias pin. The CashFlow arm of the TryParseStatement cascade
    // has FOUR aliases: "cashflow", "cash-flow", "cash flow", "cf". The
    // IntegrationTests file only exercises "income" / "wat" — the whole
    // CashFlow arm is unhit by the suite. A consolidation refactor that quietly
    // dropped the 2-character "cf" (the shortest, most prone to "looks like a
    // typo" cleanup) would route every MCP call requesting `?statement=cf` to
    // the default arm and surface "Unknown statement 'cf'" to the AI client.
    [Fact]
    public void TryParseStatement_CfAlias_ResolvesToCashFlow()
    {
        var method = typeof(FinancialStatementTools).GetMethod(
            "TryParseStatement",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var args = new object[] { "cf", null };

        var ok = (bool)method.Invoke(null, args);

        ok.Should().BeTrue();
        ((FinancialStatementType)args[1]).Should().Be(FinancialStatementType.CashFlow);
    }
}
