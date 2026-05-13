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
    public void TryExtractPaperPdfFilename_EmptyEnvelope_ReturnsFalseWithoutScanning() {
        // DocumentScraper invokes the parser on whatever the SEC EDGAR fetch
        // returned. A 404 or empty-body response yields an empty string —
        // the parser must short-circuit on null/empty BEFORE entering the
        // index-walking loop, otherwise IndexOf is invoked on an empty
        // string in a tight loop. Pin the guard so a refactor that removes
        // it surfaces immediately. The companion happy-path and traversal
        // tests don't reach this branch.
        var success = SecDocumentEnvelopeParser.TryExtractPaperPdfFilename(string.Empty, out var filename);

        success.Should().BeFalse();
        filename.Should().BeEmpty();
    }

    [Fact]
    public void TryExtractPaperPdfFilename_MultiDocumentEnvelopeWithPdfInSecondBlock_ReturnsPdfFilename() {
        // SEC envelopes regularly bundle several DOCUMENT blocks — a primary HTML/XML
        // submission followed by exhibit attachments. When a paper filing's PDF is
        // attached as a later DOCUMENT (e.g. SEQUENCE 2 EX-99 alongside a SEQUENCE 1
        // 6-K cover form), the parser must advance past the first block and keep
        // scanning. The loop does this by setting `pos = blockEnd + DocumentEndTag.Length`
        // after each iteration and re-entering at the `while (pos < envelope.Length)`
        // check. A refactor that short-circuits on the first non-PDF FILENAME (e.g.
        // returning false instead of `continue`) would compile cleanly and pass the
        // single-document happy-path test, while silently dropping every paper
        // attachment that isn't the first document in its envelope. Pin the
        // multi-block scan with a second-position PDF so that regression surfaces here.
        var envelope = """
            <SEC-DOCUMENT>
            <SEC-HEADER>
            </SEC-HEADER>
            <DOCUMENT>
            <TYPE>6-K
            <SEQUENCE>1
            <FILENAME>cover.htm
            <DESCRIPTION>Cover page
            <TEXT>
            <html><body>Cover page body</body></html>
            </TEXT>
            </DOCUMENT>
            <DOCUMENT>
            <TYPE>EX-99
            <SEQUENCE>2
            <FILENAME>exhibit99.pdf
            <DESCRIPTION>Exhibit 99
            <TEXT>
            begin 644 exhibit99.pdf
            (uuencoded body)
            end
            </TEXT>
            </DOCUMENT>
            </SEC-DOCUMENT>
            """;

        var success = SecDocumentEnvelopeParser.TryExtractPaperPdfFilename(envelope, out var filename);

        success.Should().BeTrue();
        filename.Should().Be("exhibit99.pdf");
    }

    [Fact]
    public void TryExtractPaperPdfFilename_FilenameWithBackslashPathSeparator_RejectsAndReturnsFalse() {
        // The existing path-traversal pin (#258) covers `../etc/passwd.pdf` — a Unix-style
        // traversal that fires both the leading-dot and forward-slash checks in
        // IsSafeFilename. This sibling pins the Windows-style backslash check in
        // isolation: `evil\backslash.pdf` doesn't start with `.` (so the leading-dot
        // guard is bypassed) and has no `/` (so the Unix-traversal guard is bypassed),
        // leaving ONLY the `ch == '\\'` branch as the line of defence. A refactor that
        // drops `|| ch == '\\'` from the foreach-rejection (or that swaps the OR for a
        // platform-specific Path.DirectorySeparatorChar on a non-Windows host) would
        // compile cleanly and pass the Unix-traversal sibling, while letting an SEC-
        // hosted-or-MITM'd envelope with `\` characters pierce the per-filing URL
        // sandbox on every platform. Pin the rejection on a backslash-only filename so
        // the regression surfaces here.
        var envelope = """
            <SEC-DOCUMENT>
            <SEC-HEADER>
            </SEC-HEADER>
            <DOCUMENT>
            <TYPE>6-K
            <SEQUENCE>1
            <FILENAME>evil\backslash.pdf
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
