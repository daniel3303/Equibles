using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentEnvelopeParserEncodedTraversalTests
{
    // Contract (documented in IsSafeFilename, SecDocumentEnvelopeParser.cs:61-63):
    // "EDGAR filenames are always bare names ([A-Za-z0-9._-]+); reject anything
    // that could traverse paths or escape the expected directory even though the
    // host is already locked." The candidate flows into
    // /Archives/edgar/data/{cik}/{accession}/{filename}; a URL-encoded `../../`
    // (%2e%2e%2f) is not a bare name and the server decodes it back to a real
    // traversal. Literal `../` and `\` are already pinned — the encoded form
    // (the classic allowlist-bypass) is not. Per the stated contract this must
    // be rejected: TryExtractPaperPdfFilename returns false, filename empty.
    [Fact]
    public void TryExtractPaperPdfFilename_UrlEncodedPathTraversal_RejectsAndReturnsFalse()
    {
        var envelope = """
            <SEC-DOCUMENT>
            <DOCUMENT>
            <TYPE>6-K
            <SEQUENCE>1
            <FILENAME>report%2e%2e%2f%2e%2e%2fadmin.pdf
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
