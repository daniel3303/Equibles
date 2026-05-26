using System.Reflection;
using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentHtmlNormalizerExtractSgmlTagValueFirstTokenOnlyTests
{
    // ExtractSgmlTagValue ends with `raw.Split([' ', '\t'], RemoveEmptyEntries)[0]`
    // — the first whitespace-delimited token. SEC SGML headers can carry
    // trailing description text after the canonical value, e.g.
    //   <FILENAME>exhibit-99.htm Exhibit 99.1 to Form 8-K
    // The split lifts the canonical filename out so downstream
    // EndsWith(".htm") on line 108 actually matches; without the split the
    // whole "exhibit-99.htm Exhibit 99.1..." string flows downstream, the
    // EndsWith check fails, and the entire exhibit block is silently
    // skipped from the rendered Normalize output. A refactor that "tidied"
    // the helper to `return raw;` (eliminating the seemingly redundant
    // split because "the trim already handles whitespace") would compile
    // cleanly, pass every existing TYPE-only / FILENAME-only test, and
    // silently drop every exhibit whose SGML header carries the
    // descriptive trailer SEC routinely emits.
    [Fact]
    public void ExtractSgmlTagValue_ValueFollowedByDescriptiveTrailer_ReturnsOnlyFirstToken()
    {
        var method = typeof(SecDocumentHtmlNormalizer).GetMethod(
            "ExtractSgmlTagValue",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var block = "<FILENAME>exhibit-99.htm Exhibit 99.1 to Form 8-K\n<TYPE>EX-99.1";

        var result = (string)method.Invoke(null, [block, "FILENAME"]);

        result.Should().Be("exhibit-99.htm");
    }
}
