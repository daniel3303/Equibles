using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentEnvelopeParserTests {
    [Fact]
    public void TryExtractPaperPdfFilename_FilenameWithPathTraversal_RejectsAndReturnsFalse() {
        // The parser pulls the candidate filename out of an SGML envelope body and the
        // caller uses it to compose an EDGAR URL (`/Archives/edgar/data/{cik}/{accession}/{filename}`).
        // The envelope is hostile-by-default: if SEC's CDN ever served — or a man-in-the-middle
        // ever crafted — a filing whose <FILENAME> contained `..` or a path separator, the
        // composed URL could traverse out of the per-filing directory. IsSafeFilename is the
        // guard that rejects anything starting with `.` or containing `/` `\` — drop it and the
        // URL composition is the only remaining defence. Pin the rejection on a `..` traversal
        // pattern; even though `.endswith(".pdf")` passes, the leading-`.` and embedded `/`
        // checks must both fire to return false. The companion happy-path [Fact] covers the
        // permissive side, this one covers the security side.
        var envelope = """
            <SEC-DOCUMENT>
            <SEC-HEADER>
            </SEC-HEADER>
            <DOCUMENT>
            <TYPE>6-K
            <SEQUENCE>1
            <FILENAME>../etc/passwd.pdf
            <DESCRIPTION>Form 6-K
            <TEXT>
            </TEXT>
            </DOCUMENT>
            </SEC-DOCUMENT>
            """;

        var success = SecDocumentEnvelopeParser.TryExtractPaperPdfFilename(envelope, out var filename);

        success.Should().BeFalse();
        filename.Should().BeEmpty();
    }

    [Fact]
    public void TryExtractPaperPdfFilename_EnvelopeWrappingPdfDocument_ReturnsFilename() {
        var envelope = """
            <SEC-DOCUMENT>
            <SEC-HEADER>
            <ACCEPTANCE-DATETIME>20251201170000
            </SEC-HEADER>
            <DOCUMENT>
            <TYPE>6-K
            <SEQUENCE>1
            <FILENAME>form6k.pdf
            <DESCRIPTION>Form 6-K
            <TEXT>
            begin 644 form6k.pdf
            (uuencoded body)
            end
            </TEXT>
            </DOCUMENT>
            </SEC-DOCUMENT>
            """;

        var success = SecDocumentEnvelopeParser.TryExtractPaperPdfFilename(envelope, out var filename);

        success.Should().BeTrue();
        filename.Should().Be("form6k.pdf");
    }
}
