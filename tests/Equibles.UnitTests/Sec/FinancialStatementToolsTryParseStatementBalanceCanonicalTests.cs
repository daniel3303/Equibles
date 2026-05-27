using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialStatementToolsTryParseStatementBalanceCanonicalTests
{
    // Contract (FinancialStatementTools.GetFinancialStatement Description
    // attribute, lines 56-58): the user-facing MCP docstring tells callers
    // "Statement: 'income' (income statement), 'balance' (balance sheet),
    // or 'cashflow' (cash-flow statement)." TryParseStatement's BalanceSheet
    // case-label group has FOUR fall-through aliases:
    //   "balance", "balance-sheet", "balancesheet", "bs".
    //
    // Existing sibling pins:
    //   • FinancialStatementToolsTryParseStatementBsAliasTests pins "bs".
    //   • No other BalanceSheet-arm alias is unit-pinned.
    //
    // "balance" is the SINGLE canonical user-facing form per the MCP tool
    // Description — it's what the AI client is told to send. A regression
    // that drops only the "balance" case label (the longest, most "obvious"
    // candidate for a "redundant — bs already covers it" cleanup) would
    // compile cleanly, pass the "bs" sibling pin (different label), and
    // route EVERY MCP call coming in with the documented canonical form
    // `?statement=balance` to the default arm → "Unknown statement
    // 'balance'. Use 'income', 'balance', or 'cashflow'." That error
    // message is self-referentially absurd: it tells the caller to use
    // exactly the input they just sent. The MCP client would receive a
    // confusing parse failure on the documented happy-path input while
    // every short-alias call continues to work — invisible from
    // integration tests that use "income" exclusively.
    //
    // Why this is unreachable from the existing "bs" sibling:
    //   • The "bs" pin asserts a different case label. Dropping the
    //     "balance" label leaves the "bs" label intact; the "bs" pin
    //     passes regardless of the regression.
    //   • The IncomeStatement and CashFlow arm pins target different
    //     switch arms; they can't see a BalanceSheet-arm regression
    //     either.
    //   • The default-arm pin asserts unrecognized inputs return false —
    //     it cannot distinguish "balance" label dropped (falls to default,
    //     returns false) from "label present, working correctly" because
    //     no test asserts the canonical BalanceSheet form returns true
    //     via the "balance" alias specifically.
    //
    // Pin: TryParseStatement("balance") returns (true, BalanceSheet). The
    // dual assertion (success AND enum value) distinguishes:
    //   • Label dropped → falls to default → success=false → caught by
    //     `ok.Should().BeTrue()`.
    //   • Label corrupted to wrong enum (e.g. `"balance" =>
    //     FinancialStatementType.IncomeStatement` from a copy-paste
    //     touching the wrong line) → success=true, wrong enum → caught
    //     by `Should().Be(BalanceSheet)`.
    //
    // Reflection-invoke because the helper is private static.
    [Fact]
    public void TryParseStatement_BalanceCanonicalAlias_ResolvesToBalanceSheet()
    {
        var method = typeof(FinancialStatementTools).GetMethod(
            "TryParseStatement",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var args = new object[] { "balance", null };

        var ok = (bool)method.Invoke(null, args);

        ok.Should().BeTrue();
        ((FinancialStatementType)args[1]).Should().Be(FinancialStatementType.BalanceSheet);
    }
}
