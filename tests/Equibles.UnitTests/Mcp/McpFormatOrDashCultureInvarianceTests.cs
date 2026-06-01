using System.Globalization;
using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class McpFormatOrDashCultureInvarianceTests
{
    // Adversarial Lane A. Contract (from the XML doc on McpFormat.OrDash and the
    // repo-wide MCP rule asserted by McpFormat's siblings): a non-null value MUST
    // render with InvariantCulture separators — '.' decimal, ',' thousands —
    // regardless of the host's thread CurrentCulture, so LLM-facing markdown does
    // not fork the separators by deploy locale. de-DE is the canonical hostile
    // locale used across the repo's culture pins: it swaps to '.' thousands and
    // ',' decimal, so a leak would render 1,234,567.5 as "1.234.567,5".
    [Fact]
    public void OrDash_NonNullValueUnderNonInvariantCulture_FormatsWithInvariantSeparators()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var result = McpFormat.OrDash<decimal>(1_234_567.5m, "N1");

            result
                .Should()
                .Be(
                    "1,234,567.5",
                    "McpFormat.OrDash passes CultureInfo.InvariantCulture so MCP markdown renders the same on every host locale; a non-invariant separator forks LLM-consumed output by deploy locale"
                );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
