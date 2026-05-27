using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialStatementToolsTryParseStatementUnknownDefaultTests
{
    [Fact]
    public void TryParseStatement_UnknownAlias_ReturnsFalseWithDefaultOutValue()
    {
        // Sibling to the IsAlias / PnlAlias / BsAlias / CfAlias pins. Existing
        // pins all cover MATCH arms; the default arm (`default: type = default;
        // return false`) is unpinned. A refactor that falls through to a
        // permissive default (e.g. `_ => IncomeStatement`) would compile, pass
        // every alias pin, and silently misclassify every typo-y MCP call as
        // an income-statement request — the user asked for "equity" and gets a
        // P&L. Pin the explicit-false contract on an alias that's not in the
        // map ("equity" — a real statement type but not one this tool exposes).
        var method = typeof(FinancialStatementTools).GetMethod(
            "TryParseStatement",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { "equity", default(FinancialStatementType) };
        var matched = (bool)method!.Invoke(null, args);

        matched.Should().BeFalse();
        args[1].Should().Be(default(FinancialStatementType));
    }
}
