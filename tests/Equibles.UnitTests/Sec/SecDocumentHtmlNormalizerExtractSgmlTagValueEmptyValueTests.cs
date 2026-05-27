using System.Reflection;
using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentHtmlNormalizerExtractSgmlTagValueEmptyValueTests
{
    [Fact]
    public void ExtractSgmlTagValue_TagPresentButEmptyValue_ReturnsNullNotThrow()
    {
        // ExtractSgmlTagValue ends with
        //   raw.Split([' ', '\t'], RemoveEmptyEntries)[0]
        // — indexing [0] on a split that may yield an empty array. The
        // `if (string.IsNullOrEmpty(raw)) return null;` guard above it is
        // load-bearing: an SGML tag whose value is empty (e.g. `<TYPE>\n`)
        // would Trim() down to "", split into a zero-length array under
        // RemoveEmptyEntries, and the unconditional [0] would throw
        // IndexOutOfRangeException — taking out the whole normalizer mid-loop.
        // A refactor that drops the guard on the assumption "blocks always
        // carry a value after the tag" would compile cleanly and crash the
        // first time SEC emits a tag with an empty value (which they do,
        // especially in malformed exhibits). Pin the explicit null contract.
        var method = typeof(SecDocumentHtmlNormalizer).GetMethod(
            "ExtractSgmlTagValue",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var block = "<TYPE>\n<FILENAME>foo.htm";

        var result = (string)method!.Invoke(null, [block, "TYPE"]);

        result.Should().BeNull();
    }
}
