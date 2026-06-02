using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Sec;

public class SecDocumentEnvelopeParserFallbackInlineBeatsStandaloneTests
{
    private const string InlineBody =
        "<html xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"><body><ix:nonFraction>1</ix:nonFraction></body></html>";

    private const string PlainBody = "<html><body>no inline xbrl here</body></html>";

    private const string InstanceBody = "<xbrl>standalone instance</xbrl>";

    [Fact]
    public void TryExtractXbrlEnvelope_PlainPrimaryWithSeparateInlineBlockAndInstance_PrefersFallbackInlineOverStandalone()
    {
        // Contract: when the named primary carries no inline XBRL, a non-primary
        // block that does carry inline markers must win over a standalone .INS
        // instance — the post-loop order checks the inline fallback first. The
        // failure mode this guards is wrongly returning StandaloneXbrl.
        var envelope = $"""
            <SEC-DOCUMENT>
            <DOCUMENT>
            <TYPE>10-K
            <SEQUENCE>1
            <FILENAME>acme-10k.htm
            <TEXT>
            {PlainBody}
            </TEXT>
            </DOCUMENT>
            <DOCUMENT>
            <TYPE>EX-99.1
            <SEQUENCE>2
            <FILENAME>acme-ex99.htm
            <TEXT>
            {InlineBody}
            </TEXT>
            </DOCUMENT>
            <DOCUMENT>
            <TYPE>EX-101.INS
            <SEQUENCE>7
            <FILENAME>acme-20201231.xml
            <TEXT>
            {InstanceBody}
            </TEXT>
            </DOCUMENT>
            </SEC-DOCUMENT>
            """;

        var found = SecDocumentEnvelopeParser.TryExtractXbrlEnvelope(
            envelope,
            "acme-10k.htm",
            out var type,
            out var sourceFileName,
            out var content
        );

        found.Should().BeTrue();
        type.Should().Be(XbrlType.InlineIxbrl);
        sourceFileName.Should().Be("acme-ex99.htm");
        content.Should().Be(InlineBody);
    }
}
