using System.Globalization;
using Equibles.Mcp;

namespace Equibles.UnitTests.Mcp;

/// <summary>
/// Adversarial Lane A. <c>NormalizeTicker</c> uppercases via
/// <c>ToUpperInvariant()</c> — the Invariant is load-bearing. Under
/// Turkish locale (<c>tr-TR</c>) the default <c>ToUpper()</c> maps
/// <c>'i'</c> to <c>'İ'</c> (dotted capital I, U+0130) instead of plain
/// <c>'I'</c> (U+0049). A regression that dropped the Invariant — for
/// example "the linter complained about the redundancy, ToUpper is
/// already invariant for ASCII" — would compile, pass every existing
/// ASCII-clean test, and silently break ticker lookups for any MCP host
/// running under a Turkish locale: <c>"ICE"</c> in the URL becomes
/// <c>"İCE"</c> after normalisation, which doesn't match the SQL
/// <c>Ticker = 'ICE'</c> stored row → "Stock not found" for a real ticker.
/// </summary>
public class McpToolExecutorNormalizeTickerTurkishCultureTests
{
    [Fact]
    public void NormalizeTicker_LowercaseILetterUnderTurkishCulture_UppercasesToAsciiI()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            var result = McpToolExecutor.NormalizeTicker("ice");

            result
                .Should()
                .Be(
                    "ICE",
                    "ToUpperInvariant must keep 'i' → ASCII 'I' (U+0049); a non-invariant uppercase would produce dotted 'İ' (U+0130) and break ticker lookups"
                );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
