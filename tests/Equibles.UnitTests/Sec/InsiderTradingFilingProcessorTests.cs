using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorTests {
    private static readonly MethodInfo SanitizeXmlMethod = typeof(InsiderTradingFilingProcessor)
        .GetMethod("SanitizeXml", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo ParseLongMethod = typeof(InsiderTradingFilingProcessor)
        .GetMethod("ParseLong", BindingFlags.NonPublic | BindingFlags.Static);

    [Fact]
    public void ParseLong_DecimalString_FallsBackToParseDecimalAndTruncates() {
        // SEC Form 4 XML routinely reports fractional share counts in transactionShares
        // and sharesOwnedFollowingTransaction — partial RSU vests, dividend reinvestments,
        // and ESPP fractional allocations all emit values like "1234.5678" rather than
        // a whole-share count. `long.TryParse` rejects these outright, so ParseLong falls
        // back to `(long)ParseDecimal(value)` which parses then truncates toward zero.
        // Without that fallback, every fractional-share transaction would silently store
        // a Shares=0 row, polluting position-history queries and breaking ownership
        // continuity in the holdings view.
        //
        // The risk this test pins: a refactor that drops the decimal fallback (or that
        // swaps `(long)ParseDecimal(value)` for `0`/`-1`) would compile cleanly, pass the
        // existing integration test (whose fixture XML uses whole numbers), and silently
        // zero out every partial-share row in production.
        //
        // 1234.5678 → 1234 specifically distinguishes the decimal-fallback path
        // (returns 1234) from a "truncate to 0 on parse failure" path (returns 0).
        var result = (long)ParseLongMethod.Invoke(null, ["1234.5678"]);

        result.Should().Be(1234L);
    }

    [Fact]
    public void SanitizeXml_PreservesAlreadyEscapedEntities_WhileEscapingBareAmpersand() {
        // SEC Form 3/4 XML payloads routinely contain bare `&` characters in company and
        // owner names ("Smith & Jones") that would crash XDocument.Parse. SanitizeXml's regex
        // — `&(?!(amp|lt|gt|quot|apos|#\d+|#x[\da-fA-F]+);)` — escapes those bare ampersands
        // to `&amp;` WITHOUT double-escaping anything that's already a valid XML entity.
        //
        // The risk this test pins: a "simplification" refactor to `Regex.Replace(xml, "&", "&amp;")`
        // would re-escape `&amp;` to `&amp;amp;`, silently corrupting every insider filing
        // that already used a properly-escaped entity (which the SEC does for legal names
        // like "AT&T Inc."). The corruption is invisible at sanitize time and only surfaces
        // when the parsed XML's `rptOwnerName` contains stray `amp;` characters in the DB.
        //
        // Inputs deliberately cover all six negative-lookahead alternatives plus a bare `&`.
        var input = "<XML><doc><name>AT&amp;T &lt;raw&gt; &quot;x&quot; O&apos;Brien &#65; &#x1F; Smith & Jones</name></doc></XML>";

        var result = (string)SanitizeXmlMethod.Invoke(null, [input]);

        // Bare ampersand became &amp;
        result.Should().Contain("Smith &amp; Jones");
        // Already-escaped entities are unchanged
        result.Should().Contain("AT&amp;T");
        result.Should().NotContain("&amp;amp;");
        result.Should().Contain("&lt;raw&gt;");
        result.Should().Contain("&quot;x&quot;");
        result.Should().Contain("O&apos;Brien");
        result.Should().Contain("&#65;");
        result.Should().Contain("&#x1F;");
    }
}
