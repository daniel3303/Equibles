using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

public class FactArgsTryParsePeriodNullInputTests
{
    // Sibling to FactArgsTryParsePeriodDefaultTests (which pins "Q5"
    // → false). That test covers the UNRECOGNISED-string default
    // path. This pin defends the NULL-input path, structurally
    // distinct because it must short-circuit on the `?.` null-
    // conditional in `value?.Trim().ToUpperInvariant()` BEFORE
    // reaching the switch.
    //
    // Without `?.`, calling `value.Trim()` on a null string NREs.
    // The MCP server passes user-supplied `period` arguments
    // straight through to TryParsePeriod from JSON deserialisation;
    // an omitted JSON property OR an explicit `null` produces a
    // null string. Every Equibles MCP tool that gates on
    // `if (!FactArgs.TryParsePeriod(args.Period, out var period))
    // return error("invalid period")` depends on the null path
    // returning false cleanly so the LLM gets a useful error
    // instead of an opaque server crash.
    //
    // TryParse contract (per the `Try` prefix): "never throw,
    // signal failure via the boolean return". The default arm sets
    // `period = default` and returns false; for null input the
    // switch's `value?.Trim().ToUpperInvariant()` evaluates to null
    // and falls into the `default:` case (C# switch on null
    // matches `case null:` if present, else `default:`).
    //
    // The risk this pin uniquely catches:
    //   • Drop-the-?. regression — `value.Trim().ToUpperInvariant()`
    //     under "the caller will never pass null" — would NRE on
    //     every null MCP argument, surfacing as a JSON-RPC
    //     InternalError in the MCP transport that's hard to
    //     attribute to a missing tool argument.
    //   • Wrong-default regression — `period = SecFiscalPeriod.
    //     FullYear` in the default arm (someone "fixes" the
    //     out-param to a "sensible" default rather than the
    //     enum zero value) — would silently classify null/garbage
    //     period inputs as full-year filings instead of failing.
    //     The default-Q5 sibling test could catch this if it
    //     asserted `period == default(SecFiscalPeriod)` (which
    //     happens to equal FullYear=0 — the enum's first member
    //     by convention here), but it asserts `_` (out var
    //     discard), so it doesn't.
    //
    // Pin: dual assertion — false return AND default period —
    // distinguishes both regressions.
    [Fact]
    public void TryParsePeriod_NullInput_ReturnsFalseAndDefaultPeriodWithoutThrowing()
    {
        var success = FactArgs.TryParsePeriod(null, out var period);

        success.Should().BeFalse();
        period.Should().Be(default(SecFiscalPeriod));
    }
}
