using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialStatementToolsTryParseStatementPAmpersandLAliasTests
{
    // Completes the IncomeStatement arm coverage alongside the existing
    // pins (income/is/pnl). The "p&l" case label is unique in the
    // entire TryParseStatement switch: it's the ONLY label that
    // contains a special character — the ampersand `&`.
    //
    // The risks this pin uniquely catches:
    //
    //   • Sanitize-before-parse layer regression — a refactor that
    //     introduces input sanitisation (HTML-escape, URL-decode,
    //     character allowlist) before TryParseStatement is called
    //     would convert "p&l" into something the case label can no
    //     longer match: "p&amp;l" after HTML-encoding, "p l" after
    //     "strip non-alnum" naïve cleansing. The existing pnl pin
    //     would still pass (no special char) — only this pin
    //     catches the regression.
    //
    //   • Drop-the-ampersand-label regression — `case "p&l":` is the
    //     only label that looks "suspicious" (ASCII-noise) so it's
    //     the most likely target of a "let's simplify the aliases"
    //     cleanup. Drop it and every analyst who types the natural
    //     financial-reporting shorthand "P&L" hits the default arm.
    //
    //   • ToLowerInvariant normalisation regression — the switch
    //     pre-condition is `value?.Trim().ToLowerInvariant()`. A
    //     refactor that swaps in `ToLower()` (culture-sensitive) is
    //     covered by other "income"/"balance"/"cashflow" pins, but
    //     a regression that drops case normalisation entirely
    //     would leave `"P&L"` unmatched against `"p&l"` while ASCII
    //     case-folded inputs ("PNL" → "pnl") still match the
    //     uppercase-equivalent label. The "P&L" input here exercises
    //     both case normalisation AND the special-character label.
    //
    // Adversarial input: uppercase "P&L" — common analyst shorthand
    // for income statement, the canonical Wall Street naming. The
    // ToLowerInvariant normalisation converts it to "p&l" which must
    // match the case label. Assert both axes (true + IncomeStatement).
    [Fact]
    public void TryParseStatement_PAmpersandLUppercase_ResolvesToIncomeStatementViaCaseAndAmpersandLabel()
    {
        var method = typeof(FinancialStatementTools).GetMethod(
            "TryParseStatement",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var args = new object[] { "P&L", null };

        var ok = (bool)method.Invoke(null, args);

        ok.Should().BeTrue();
        ((FinancialStatementType)args[1]).Should().Be(FinancialStatementType.IncomeStatement);
    }
}
