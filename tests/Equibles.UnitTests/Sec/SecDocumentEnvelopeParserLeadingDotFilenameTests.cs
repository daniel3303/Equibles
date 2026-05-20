using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentEnvelopeParserLeadingDotFilenameTests
{
    // Contract (IsSafeFilename, SecDocumentEnvelopeParser.cs:64-68): a candidate
    // whose first character is '.' must be rejected. Dot is the leading
    // character of "../" traversal sequences and of Unix hidden-file conventions;
    // the bare-name allowlist alone permits dots anywhere, so the explicit
    // leading-dot rejection is the defense in depth. Sibling to the encoded-
    // traversal, whitespace, and backslash pins. A regression that dropped this
    // single line would let "..pdf" / ".hidden.pdf" through and flow into the
    // /Archives/edgar/data/{cik}/{accession}/{filename} URL.
    [Fact]
    public void TryExtractPaperPdfFilename_FilenameStartingWithDot_RejectsAndReturnsFalse()
    {
        var envelope = """
            <SEC-DOCUMENT>
            <DOCUMENT>
            <TYPE>6-K
            <SEQUENCE>1
            <FILENAME>.hidden.pdf
            <TEXT>
            </TEXT>
            </DOCUMENT>
            </SEC-DOCUMENT>
            """;

        var success = SecDocumentEnvelopeParser.TryExtractPaperPdfFilename(
            envelope,
            out var filename
        );

        success.Should().BeFalse();
        filename.Should().BeEmpty();
    }
}
