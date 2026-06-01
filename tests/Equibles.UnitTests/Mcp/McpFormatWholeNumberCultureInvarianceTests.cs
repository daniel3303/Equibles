using System.Globalization;
using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class McpFormatWholeNumberCultureInvarianceTests
{
    // Adversarial Lane A. Contract (from the XML doc on McpFormat.WholeNumber): a whole
    // number MUST render with InvariantCulture grouping — ',' thousands separator —
    // regardless of the host's thread CurrentCulture, so LLM-facing markdown does not fork
    // the separators by deploy locale. de-DE is the canonical hostile locale used across the
    // repo's culture pins: it swaps the group separator to '.', so a leak would render
    // 1,234,567 as "1.234.567" — the exact MCP culture-forking regression this guards.
    [Fact]
    public void WholeNumber_UnderNonInvariantCulture_FormatsWithInvariantThousandsSeparators()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var result = McpFormat.WholeNumber(1_234_567L);

            result
                .Should()
                .Be(
                    "1,234,567",
                    "McpFormat.WholeNumber passes CultureInfo.InvariantCulture so MCP markdown renders the same on every host locale; a non-invariant group separator forks LLM-consumed output by deploy locale"
                );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
