using System.Reflection;
using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentEnvelopeParserIsSafeFilenameEmptyTests
{
    // Sibling to SecDocumentEnvelopeParserLeadingDotFilename / EncodedTraversal /
    // WhitespaceFilename pins. IsSafeFilename is the security gate that keeps
    // an untrusted envelope-supplied filename from flowing into a URL — its
    // first check is `value.Length == 0`. The public TryExtractPaperPdfFilename
    // currently never reaches IsSafeFilename with an empty value (its caller
    // splits-on-whitespace and discards empty entries), but that's a defensive
    // contract that any future caller (or refactor of the SGML extraction)
    // depends on. A regression to `value[0] == '.'` as the first check would
    // throw IndexOutOfRangeException on an empty input. Pin the empty-string
    // safety net via reflection.
    [Fact]
    public void IsSafeFilename_EmptyString_ReturnsFalseWithoutThrowing()
    {
        var method = typeof(SecDocumentEnvelopeParser).GetMethod(
            "IsSafeFilename",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        bool result = true;
        var act = () => result = (bool)method.Invoke(null, [string.Empty]);

        act.Should().NotThrow();
        result.Should().BeFalse();
    }
}
