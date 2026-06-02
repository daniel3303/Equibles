using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Sec;

public class SecDocumentEnvelopeParserFallbackInlineMissingFilenameTests
{
    // Contract: the fallback-inline path must capture a block carrying inline ix: markers even
    // when the primary name is missing or doesn't match — otherwise the filing is wrongly recorded
    // as not-present. A block that omits its <FILENAME> tag entirely is the unguarded edge of that
    // rule: detection must still succeed and the absent name must surface as a non-null empty
    // string, not a lost fact or a throw.
    [Fact]
    public void TryExtractXbrlEnvelope_InlineBlockMissingFilenameTag_StillReturnsInlineWithEmptySource()
    {
        const string inlineBody =
            "<html xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"><body>"
            + "<ix:nonFraction>1</ix:nonFraction></body></html>";

        // The block carries inline markers but has no <FILENAME>, and the primary name passed in
        // matches nothing, so the named-primary fast path cannot fire.
        var envelope = $"""
            <SEC-DOCUMENT>
            <DOCUMENT>
            <TYPE>10-K
            <SEQUENCE>1
            <TEXT>
            {inlineBody}
            </TEXT>
            </DOCUMENT>
            </SEC-DOCUMENT>
            """;

        var found = SecDocumentEnvelopeParser.TryExtractXbrlEnvelope(
            envelope,
            primaryDocumentFileName: "acme-10k.htm",
            out var type,
            out var sourceFileName,
            out var content
        );

        found.Should().BeTrue();
        type.Should().Be(XbrlType.InlineIxbrl);
        sourceFileName.Should().BeEmpty();
        content.Should().Be(inlineBody);
    }
}
