using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialStatementToolsTryParseStatementCashFlowWithSpaceTests
{
    // Completes the CashFlow arm coverage. Existing sibling pin
    // FinancialStatementToolsTryParseStatementCfAliasTests pins "cf"
    // (short form). The CashFlow case-label group has FOUR aliases:
    //   "cashflow", "cash-flow", "cash flow", "cf"
    //
    // The "cash flow" alias is unique in the entire switch — it's
    // the ONLY case label that contains an INTERIOR whitespace
    // character. The two other multi-word labels in adjacent
    // statement arms ("income-statement", "balance-sheet") use a
    // hyphen separator; only this one is space-separated. That
    // makes it the most likely target of an over-eager "normalise
    // whitespace" refactor:
    //
    //   • Strip-internal-whitespace regression — a refactor that
    //     pre-processes input via `Regex.Replace(value, @"\s+", "")`
    //     (under "be tolerant of typos") would convert "cash flow"
    //     → "cashflow", which DOES match the adjacent "cashflow"
    //     label, so the "cf"/"cashflow" pins still pass. But the
    //     refactor would ALSO collapse other multi-word labels —
    //     `"income statement"` → `"incomestatement"` which DOES
    //     match. Subtle and possibly benign for CashFlow. The
    //     drop-this-case regression is the sharper attack.
    //
    //   • Drop-the-case-label regression — `case "cash flow":` is
    //     the "most redundant" looking entry (humans type either
    //     the hyphenated, no-separator, or short form; the
    //     space-separated form is a documentation typo). Drop it
    //     under "no caller uses this" and any LLM that produces
    //     `"cash flow"` (the most natural English form) hits the
    //     default arm. The existing "cf" pin keeps passing because
    //     it targets a different label.
    //
    //   • Trim-too-aggressive regression — a refactor that did
    //     `value?.Replace(" ", "").Trim()...` would collapse
    //     "Cash Flow" → "CashFlow" → "cashflow" → still matches.
    //     Robust against this specific regression. The pin's value
    //     comes from defending the LITERAL case label as written.
    //
    // Adversarial input: title-case "Cash Flow" — the natural
    // English form an LLM produces most often. The ToLowerInvariant
    // normalisation converts to "cash flow"; the interior space
    // means it MUST match the "cash flow" label specifically (not
    // any neighbour). Dual assertion (true + CashFlow) catches
    // drop and swap.
    [Fact]
    public void TryParseStatement_CashFlowTitleCaseWithInteriorSpace_ResolvesToCashFlow()
    {
        var method = typeof(FinancialStatementTools).GetMethod(
            "TryParseStatement",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var args = new object[] { "Cash Flow", null };

        var ok = (bool)method.Invoke(null, args);

        ok.Should().BeTrue();
        ((FinancialStatementType)args[1]).Should().Be(FinancialStatementType.CashFlow);
    }
}
