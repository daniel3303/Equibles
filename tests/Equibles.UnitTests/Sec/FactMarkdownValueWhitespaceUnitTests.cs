using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

public class FactMarkdownValueWhitespaceUnitTests
{
    [Fact]
    public void Value_UsdUnitWithSurroundingWhitespace_StillTreatedAsUsd()
    {
        // FactMarkdown.Value begins with `var u = unit?.Trim()` so the
        // downstream `StartsWith("USD")` / `Equals("shares")` / IsBareCurrency
        // checks ignore SEC payloads that ship with stray whitespace
        // (XBRL units occasionally carry a trailing newline or surrounding
        // space when the filer's serializer emitted formatted XML). A
        // refactor dropping the `.Trim()` (e.g. "the upstream parser
        // already strips whitespace") would compile, pass every existing
        // bare/per-share pin (those use clean unit strings), and route
        // " USD " through the pure-dimensionless branch — rendering
        // "1234567" without the "$" prefix or grouped separators on every
        // whitespace-padded USD fact. Pin the Trim semantic with a real
        // shape: leading + trailing space around USD must still produce
        // the canonical "$N,NNN,NNN" output.
        var result = FactMarkdown.Value(1234567m, " USD ");

        result.Should().Be("$1,234,567");
    }
}
