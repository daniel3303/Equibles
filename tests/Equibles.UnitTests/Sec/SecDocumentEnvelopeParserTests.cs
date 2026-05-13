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
    public void TryExtractPaperPdfFilename_DocumentStartWithoutEndTag_ReturnsFalseInsteadOfThrowing() {
        // SEC EDGAR responses can be truncated by upstream proxies, transient TCP
        // resets, or partial reads on the scraper side. When the envelope body
        // contains `<DOCUMENT>` but no matching `</DOCUMENT>`, the loop's
        // `envelope.IndexOf(DocumentEndTag, blockStart, ...)` returns -1 — and
        // the `if (blockEnd == -1) return false;` guard is the only thing
        // preventing the next line, `envelope.Substring(blockStart, blockEnd -
        // blockStart + DocumentEndTag.Length)`, from being called with a
        // negative length and throwing ArgumentOutOfRangeException. A refactor
        // that drops the guard (e.g. assuming a well-formed envelope, or
        // replacing the explicit check with a defensive Math.Max that masks
        // the truncation) would compile cleanly, pass every existing test
        // (all complete envelopes), and then crash the DocumentScraper on the
        // first partial response — which is exactly the moment we want
        // structured "no paper PDF here" handling, not a thrown exception
        // bubbling up to BaseScraperWorker. Pin the silent-false contract.
        var envelope = """
            <SEC-DOCUMENT>
            <SEC-HEADER>
            </SEC-HEADER>
            <DOCUMENT>
            <TYPE>6-K
            <SEQUENCE>1
            <FILENAME>form6k.pdf
            <DESCRIPTION>Form 6-K
            <TEXT>
            begin 644 form6k.pdf
            (truncated mid-stream — closing DOCUMENT tag never arrives)
            """;

        var success = SecDocumentEnvelopeParser.TryExtractPaperPdfFilename(envelope, out var filename);

        success.Should().BeFalse();
        filename.Should().BeEmpty();
    }

    [Fact]
    public void TryExtractPaperPdfFilename_FilenameStartingWithDotNoPathSeparators_RejectsAndReturnsFalse() {
        // IsSafeFilename's defensive guard has three independent arms:
        //   1. `value.Length == 0` → return false
        //   2. `value[0] == '.'` → return false (this pin)
        //   3. foreach `ch == '/'` or `ch == '\\'` → return false
        // The existing path-traversal pin (`../etc/passwd.pdf`) exercises arms
        // 2 AND 3 simultaneously: the leading dot AND the embedded slash both
        // independently reject the filename. The Windows-backslash pin
        // (`evil\backslash.pdf`) isolates arm 3 alone. NO existing pin isolates
        // arm 2 — the leading-dot guard — without also tripping a path-
        // separator check.
        //
        // The risk: a refactor that "tidies up" the redundant-looking
        // `value[0] == '.'` check (under the false intuition that "leading
        // dots only show up alongside `..` traversals, which the slash guard
        // already catches") would compile cleanly, pass BOTH existing
        // path-traversal pins (those have slashes), and silently let a
        // dotfile-style filename like `.env.pdf`, `.htaccess.pdf`, or
        // `.aws/credentials.pdf` (sans-slash variants) through into URL
        // composition. On a server that ever served EDGAR mirror content
        // out of a writable directory, that filename would compose to
        //   /Archives/edgar/data/{cik}/{accession}/.env.pdf
        // which on Apache/Nginx default configs reads from a hidden file
        // the operator never intended to expose. SEC's own CDN isn't
        // affected today, but the guard is defence-in-depth — the parser
        // doesn't know who is composing the downstream URL.
        //
        // Pin a leading-dot filename with NO path separator characters so
        // arm 2 fires in isolation. `.env.pdf` ends with `.pdf` (passes
        // the EndsWith check on line 31), contains no `/` or `\\` (bypasses
        // arm 3), and has no `..` (bypasses dot-double-dot heuristics
        // that aren't in the guard). The only line that rejects this
        // input is arm 2; if it disappears, this test fails.
        var envelope = """
            <SEC-DOCUMENT>
            <SEC-HEADER>
            </SEC-HEADER>
            <DOCUMENT>
            <TYPE>6-K
            <SEQUENCE>1
            <FILENAME>.env.pdf
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
