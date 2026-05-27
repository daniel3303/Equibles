using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

public class FactArgsTryParsePeriodFullYearAliasTests
{
    // Completes the FY/FULLYEAR/ANNUAL alias trio. Existing siblings:
    //   • FactArgsTryParsePeriodFyCanonicalAliasTests pins "FY".
    //   • FactArgsTryParsePeriodAnnualAliasTests pins lowercase
    //     "annual" — exercises ANNUAL label via ToUpperInvariant.
    // The middle alias FULLYEAR is the ONLY full-year case label
    // not directly pinned. Each label is a distinct fall-through
    // case in the switch — a drop or swap of one label leaves the
    // other two intact, so the trio's coverage requires explicit
    // pins on each.
    //
    // Why FULLYEAR specifically warrants its own pin:
    //   • Production trigger — LLM-generated MCP arguments often
    //     spell the human-readable form. Claude/GPT/Gemini are
    //     trained on financial reporting language where "full
    //     year" is the natural English form ("Apple's full-year
    //     2024 revenue"). The LLM produces `"FullYear"` (camel
    //     case from C# enum naming exposure) far more often
    //     than the SEC wire form "FY". A drop of this label
    //     specifically would degrade the MCP tool's robustness
    //     against LLM input naming variance.
    //
    //   • Drop-the-FULLYEAR-label regression — leaves FY and
    //     ANNUAL intact, passes both sibling pins, silently
    //     rejects every LLM-natural "FullYear" input. The MCP
    //     tool emits "invalid period" errors on inputs the
    //     doc-comment promises to accept.
    //
    //   • Swap-the-FULLYEAR-label regression — `case "FULLYEAR"
    //     => SecFiscalPeriod.Q4` (a careless edit during a Q4-
    //     vs-FullYear distinction refactor) would route FULLYEAR
    //     inputs to Q4 instead of FullYear. Asserting the EXACT
    //     period value catches this.
    //
    // Pin: input "FullYear" (camel case — proves both the case
    // label fires AND that ToUpperInvariant normalises correctly
    // to match the label). Assert (true, FullYear) — the dual
    // assertion distinguishes drop-the-label (success=false)
    // from swap-the-label (success=true with wrong period).
    [Fact]
    public void TryParsePeriod_FullYearCamelCaseAlias_ReturnsTrueAndFullYear()
    {
        var success = FactArgs.TryParsePeriod("FullYear", out var period);

        success.Should().BeTrue();
        period.Should().Be(SecFiscalPeriod.FullYear);
    }
}
