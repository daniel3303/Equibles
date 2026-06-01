using System.Globalization;
using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class McpFormatOrDashNullTests
{
    // Adversarial Lane A. Contract (XML doc on McpFormat.OrDash): "Formats a nullable
    // value with the given format string, OR the em-dash placeholder when null." The
    // null branch is the unexercised half of OrDash — its sibling test only covers a
    // present value. A correct impl must short-circuit on null and return exactly the
    // placeholder (U+2014), independent of the format string passed in. The format
    // below is a marker that would appear in the output if the null guard ever leaked
    // and applied it as a custom numeric format; the assertion that we get a bare
    // em-dash proves it did not. A hostile host locale (de-DE) is pinned to show the
    // placeholder is culture-independent too.
    [Fact]
    public void OrDash_NullValue_ReturnsEmDashPlaceholderRegardlessOfFormat()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var result = McpFormat.OrDash<decimal>(null, @"\L\E\A\K");

            result
                .Should()
                .Be(
                    "—",
                    "OrDash promises the em-dash placeholder for a null value, short-circuiting before the format string is ever applied"
                );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
