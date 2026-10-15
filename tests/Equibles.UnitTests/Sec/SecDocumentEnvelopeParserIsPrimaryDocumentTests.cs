using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

// EDGAR lists a submission's primary document first; only that document is the filing's original.
public class SecDocumentEnvelopeParserIsPrimaryDocumentTests
{
    private const string Envelope = """
        <SEC-DOCUMENT>0000123456-26-000008.txt
        <DOCUMENT>
        <TYPE>ARS
        <FILENAME>acme_ars.htm
        <TEXT>
        </TEXT>
        </DOCUMENT>
        <DOCUMENT>
        <TYPE>EX-99
        <FILENAME>acme_ex99.pdf
        <TEXT>
        </TEXT>
        </DOCUMENT>
        """;

    [Theory]
    [InlineData("acme_ars.htm", true)]
    [InlineData("ACME_ARS.HTM", true)]
    [InlineData("acme_ex99.pdf", false)]
    [InlineData("missing.pdf", false)]
    public void IsPrimaryDocument_IsTheFirstBlockOnly(string filename, bool expected)
    {
        SecDocumentEnvelopeParser.IsPrimaryDocument(Envelope, filename).Should().Be(expected);
    }

    [Fact]
    public void IsPrimaryDocument_EnvelopeWithoutBlocks_IsFalse()
    {
        SecDocumentEnvelopeParser.IsPrimaryDocument("", "acme.pdf").Should().BeFalse();
    }
}
