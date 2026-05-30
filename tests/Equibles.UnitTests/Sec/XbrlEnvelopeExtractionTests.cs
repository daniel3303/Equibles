using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Sec;

public class XbrlEnvelopeExtractionTests
{
    private const string InlinePrimaryBody =
        "<html xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"><body><ix:nonFraction>1</ix:nonFraction></body></html>";

    private const string PlainPrimaryBody = "<html><body>no inline xbrl here</body></html>";

    private const string InstanceBody = "<xbrl>standalone instance</xbrl>";

    private static string Submission(string primaryFileName, string primaryBody, bool withInstance)
    {
        var instanceBlock = withInstance
            ? $"""
                <DOCUMENT>
                <TYPE>EX-101.INS
                <SEQUENCE>7
                <FILENAME>acme-20201231.xml
                <TEXT>
                {InstanceBody}
                </TEXT>
                </DOCUMENT>
                """
            : string.Empty;

        return $"""
            <SEC-DOCUMENT>
            <DOCUMENT>
            <TYPE>10-K
            <SEQUENCE>1
            <FILENAME>{primaryFileName}
            <TEXT>
            {primaryBody}
            </TEXT>
            </DOCUMENT>
            {instanceBlock}
            </SEC-DOCUMENT>
            """;
    }

    [Fact]
    public void TryExtractXbrlEnvelope_PrimaryHasInlineXbrl_ReturnsInlineIxbrl()
    {
        var envelope = Submission("acme-10k.htm", InlinePrimaryBody, withInstance: false);

        var found = SecDocumentEnvelopeParser.TryExtractXbrlEnvelope(
            envelope,
            "acme-10k.htm",
            out var type,
            out var sourceFileName,
            out var content
        );

        found.Should().BeTrue();
        type.Should().Be(XbrlType.InlineIxbrl);
        sourceFileName.Should().Be("acme-10k.htm");
        content.Should().Be(InlinePrimaryBody);
    }

    [Fact]
    public void TryExtractXbrlEnvelope_PrimaryHasInlineAndInstancePresent_PrefersInline()
    {
        var envelope = Submission("acme-10k.htm", InlinePrimaryBody, withInstance: true);

        var found = SecDocumentEnvelopeParser.TryExtractXbrlEnvelope(
            envelope,
            "acme-10k.htm",
            out var type,
            out _,
            out var content
        );

        found.Should().BeTrue();
        type.Should().Be(XbrlType.InlineIxbrl);
        content.Should().Be(InlinePrimaryBody);
    }

    [Fact]
    public void TryExtractXbrlEnvelope_PlainPrimaryButInstancePresent_ReturnsStandaloneXbrl()
    {
        var envelope = Submission("acme-10k.htm", PlainPrimaryBody, withInstance: true);

        var found = SecDocumentEnvelopeParser.TryExtractXbrlEnvelope(
            envelope,
            "acme-10k.htm",
            out var type,
            out var sourceFileName,
            out var content
        );

        found.Should().BeTrue();
        type.Should().Be(XbrlType.StandaloneXbrl);
        sourceFileName.Should().Be("acme-20201231.xml");
        content.Should().Be(InstanceBody);
    }

    [Fact]
    public void TryExtractXbrlEnvelope_NoInlineAndNoInstance_ReturnsFalse()
    {
        var envelope = Submission("acme-10k.htm", PlainPrimaryBody, withInstance: false);

        var found = SecDocumentEnvelopeParser.TryExtractXbrlEnvelope(
            envelope,
            "acme-10k.htm",
            out _,
            out _,
            out _
        );

        found.Should().BeFalse();
    }

    [Fact]
    public void TryExtractXbrlEnvelope_EmptyEnvelope_ReturnsFalse()
    {
        SecDocumentEnvelopeParser
            .TryExtractXbrlEnvelope(string.Empty, "acme-10k.htm", out _, out _, out _)
            .Should()
            .BeFalse();
    }
}
