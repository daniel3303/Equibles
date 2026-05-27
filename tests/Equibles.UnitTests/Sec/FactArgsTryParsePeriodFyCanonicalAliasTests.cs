using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

public class FactArgsTryParsePeriodFyCanonicalAliasTests
{
    // Contract (FactArgs.TryParsePeriod doc-comment + the FY case-label
    // group at lines 15-19): the parser accepts THREE fall-through case
    // labels for the full-year period — "FY", "FULLYEAR", "ANNUAL" —
    // before returning SecFiscalPeriod.FullYear.
    //
    // Existing siblings:
    //   • FactArgsTryParsePeriodAnnualAliasTests pins lowercase "annual"
    //     → FullYear (covers the ANNUAL label PLUS ToUpperInvariant).
    //   • FactArgsTryParsePeriodTests pins "Q4" → Q4.
    //   • Q1, Q2, Q3 each have dedicated sibling pins.
    //
    // The "FY" case label specifically is the SEC's CANONICAL wire form.
    // The SEC Company Facts API emits `fp = "FY"` on every full-year
    // fact in the entire taxonomy — every 10-K filed since the API
    // launched. This is the highest-volume input by orders of magnitude
    // (every annual fact for every public company since ~2014). A
    // regression that drops the "FY" case label specifically would
    // compile cleanly, pass the ANNUAL sibling pin (different label),
    // pass every quarter sibling (different periods), and silently
    // reject EVERY full-year fact in the entire production pipeline —
    // the income-statement-by-year MCP tool would return empty results
    // for every company, the historical-comparison view would render
    // blank annual columns, and the operator would only notice via a
    // user-reported "where are the annual numbers" complaint.
    //
    // Why this specifically is unreachable from the existing siblings:
    //   • The ANNUAL pin uses lowercase "annual" — after
    //     ToUpperInvariant it hits the "ANNUAL" case label. Dropping
    //     the "FY" label leaves "ANNUAL" intact, so the ANNUAL pin
    //     passes.
    //   • The Q1/Q2/Q3/Q4 pins all target different case labels;
    //     they're untouched by an "FY" label drop.
    //   • The default-arm pin covers unrecognized inputs returning
    //     false — it cannot distinguish "FY case dropped, falls to
    //     default" from "FY case present, but caller-side bug" because
    //     no test asserts FY → FullYear returns true.
    //
    // Pin: TryParsePeriod("FY") returns (true, FullYear). The exact
    // uppercase wire form (no case conversion needed) — proves the
    // case label fires directly, no normalization dependency. Asserting
    // both `success == true` AND `period == FullYear` distinguishes
    // "label dropped, fell through to default" (success=false) from
    // "label corrupted to a wrong enum" (success=true, period=wrong).
    [Fact]
    public void TryParsePeriod_FyCanonicalUppercase_ReturnsTrueAndFullYear()
    {
        var success = FactArgs.TryParsePeriod("FY", out var period);

        success.Should().BeTrue();
        period.Should().Be(SecFiscalPeriod.FullYear);
    }
}
