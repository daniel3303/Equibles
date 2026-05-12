using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorTests {
    private static readonly MethodInfo SanitizeXmlMethod = typeof(InsiderTradingFilingProcessor)
        .GetMethod("SanitizeXml", BindingFlags.NonPublic | BindingFlags.Static);

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
