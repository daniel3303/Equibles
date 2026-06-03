using System.Globalization;
using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class McpFormatInvariantCultureInvarianceTests
{
    // Contract (XML doc on McpFormat.Invariant): formats a value with the given format string in
    // invariant culture so MCP markdown does not fork the separators by host locale. The OrDash and
    // WholeNumber siblings carry de-DE pins; Invariant — the non-nullable companion — has none. Under
    // de-DE the thread default swaps '.'/',' so a current-culture format would fork LLM-consumed output.
    [Fact]
    public void Invariant_UnderNonInvariantCulture_FormatsWithInvariantSeparators()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var result = McpFormat.Invariant(1_234_567.5m, "N1");

            result.Should().Be("1,234,567.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
