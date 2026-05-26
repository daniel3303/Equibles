using System.Reflection;
using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentHtmlNormalizerExtractInnerContentMissingCloseTests
{
    // ExtractInnerContent is used by Normalize to pull the body of `<XBRL>...
    // </XBRL>` or `<TEXT>...</TEXT>` envelopes out of SEC's SGML wrapper. The
    // contract is "return inner content only if BOTH open and close tags are
    // found"; the callsite falls back to the raw block when this helper
    // returns null. A refactor that "simplified" the close-tag-missing arm
    // to `return block.Substring(contentStart)` (returning the rest of the
    // buffer when no close tag) would compile cleanly and silently emit
    // everything from the open tag to end-of-block — junk content beyond what
    // SEC sealed inside the envelope, polluting downstream HTML normalization.
    [Fact]
    public void ExtractInnerContent_OpenTagPresentButNoCloseTag_ReturnsNullNotPartialContent()
    {
        var method = typeof(SecDocumentHtmlNormalizer).GetMethod(
            "ExtractInnerContent",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method.Invoke(null, ["<XBRL>some data but no close", "XBRL"]);

        result.Should().BeNull();
    }
}
