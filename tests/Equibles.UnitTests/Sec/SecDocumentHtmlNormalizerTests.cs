using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentHtmlNormalizerTests {
    private readonly SecDocumentHtmlNormalizer _sut = new();

    [Fact]
    public void Normalize_NullInput_ReturnsEmptyString() {
        var result = _sut.Normalize(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_EmptyString_ReturnsEmptyString() {
        var result = _sut.Normalize(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_Whitespace_ReturnsEmptyString() {
        var result = _sut.Normalize("   \t\n  ");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_ValidSgmlWithKnownType_ReturnsNormalizedHtml() {
        var sgml = """
            <DOCUMENT>
            <TYPE>10-K
            <FILENAME>filing.htm
            <TEXT>
            <html><body><p>Annual Report</p></body></html>
            </TEXT>
            </DOCUMENT>
            """;

        var result = _sut.Normalize(sgml);

        result.Should().Contain("Annual Report");
    }

    [Fact]
    public void Normalize_UnknownDocumentType_ReturnsEmptyString() {
        var sgml = """
            <DOCUMENT>
            <TYPE>UNKNOWN-TYPE
            <FILENAME>filing.htm
            <TEXT>
            <html><body><p>Should be filtered</p></body></html>
            </TEXT>
            </DOCUMENT>
            """;

        var result = _sut.Normalize(sgml);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_ExhibitTypeUnder100_IsAllowed() {
        var sgml = """
            <DOCUMENT>
            <TYPE>EX-21
            <FILENAME>exhibit.htm
            <TEXT>
            <html><body><p>Subsidiary List</p></body></html>
            </TEXT>
            </DOCUMENT>
            """;

        var result = _sut.Normalize(sgml);

        result.Should().Contain("Subsidiary List");
    }

    [Fact]
    public void Normalize_ExhibitTypeWithNonNumericSuffix_IsFilteredWithoutThrowing() {
        // IsAllowedDocumentType's exhibit branch:
        //   if (documentType.StartsWith("EX-")) {
        //       var exNumberPart = documentType.Substring(3);
        //       var exNumberPartClean = exNumberPart.Split('.')[0];
        //       if (int.TryParse(exNumberPartClean, out var exNumber) && exNumber < 100) {
        //           return true;
        //       }
        //   }
        //   return false;
        // The existing `Normalize_ExhibitTypeOver100_IsFiltered` pin exercises the
        // path where TryParse succeeds (999 parses fine) but `< 100` fails — falls
        // through to `return false`. The TryParse=FALSE branch is structurally
        // distinct and unpinned: when the suffix is non-numeric (e.g. an aggregator
        // adds a metadata suffix, or a malformed EDGAR upload labels an exhibit
        // "EX-A1" or "EX-foo"), TryParse returns false, the short-circuit AND
        // skips the < 100 comparison, and the method falls through to `return false`.
        //
        // The risk this catches: a refactor that "modernizes" `int.TryParse` to
        // `int.Parse` (assuming SEC always sends a valid number) would throw
        // FormatException on the first non-numeric EX-* code. There's no try/catch
        // around the steps in Normalize, so the exception would bubble up through
        // ExtractAndFilterDocuments → Normalize → the caller (typically the
        // DocumentScraper.CreateDocument path), aborting the import of that filing
        // and potentially affecting the whole batch.
        //
        // Pin: an EX-* document with a non-numeric suffix. Result must be empty
        // (filtered), not thrown. The trailing valid 10-K confirms Normalize
        // continues past the malformed exhibit and processes the next document
        // — proving the loop's break didn't fire and the catch-less code path
        // returned cleanly.
        var sgml = """
            <DOCUMENT>
            <TYPE>EX-foo
            <FILENAME>exhibit.htm
            <TEXT>
            <html><body><p>Malformed exhibit code</p></body></html>
            </TEXT>
            </DOCUMENT>
            <DOCUMENT>
            <TYPE>10-K
            <FILENAME>filing.htm
            <TEXT>
            <html><body><p>Real filing follows</p></body></html>
            </TEXT>
            </DOCUMENT>
            """;

        var result = _sut.Normalize(sgml);

        result.Should().NotContain("Malformed exhibit code");
        result.Should().Contain("Real filing follows");
    }

    [Fact]
    public void Normalize_ExhibitTypeOver100_IsFiltered() {
        var sgml = """
            <DOCUMENT>
            <TYPE>EX-999
            <FILENAME>exhibit.htm
            <TEXT>
            <html><body><p>Should be filtered</p></body></html>
            </TEXT>
            </DOCUMENT>
            """;

        var result = _sut.Normalize(sgml);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_FileWithoutAllowedExtension_IsFiltered() {
        var sgml = """
            <DOCUMENT>
            <TYPE>10-K
            <FILENAME>filing.pdf
            <TEXT>
            <html><body><p>Should be filtered</p></body></html>
            </TEXT>
            </DOCUMENT>
            """;

        var result = _sut.Normalize(sgml);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_MultipleDocuments_KeepsKnownFilterUnknown() {
        var sgml = """
            <DOCUMENT>
            <TYPE>10-K
            <FILENAME>annual.htm
            <TEXT>
            <html><body><p>Annual Report</p></body></html>
            </TEXT>
            </DOCUMENT>
            <DOCUMENT>
            <TYPE>GRAPHIC
            <FILENAME>image.jpg
            <TEXT>
            binary content here
            </TEXT>
            </DOCUMENT>
            """;

        var result = _sut.Normalize(sgml);

        result.Should().Contain("Annual Report");
        result.Should().NotContain("binary content here");
    }

    [Fact]
    public void Normalize_BlockWithoutXbrlOrTextWrapper_FallsBackToFullBlock() {
        // ExtractAndFilterDocuments composes content as
        //   ExtractInnerContent("XBRL") ?? ExtractInnerContent("TEXT") ?? block.
        // The existing tests cover the XBRL and TEXT wrappers; the final
        // fallback (whole block when neither wrapper is present) was
        // unpinned. Some legacy SEC SGML filings carry inline HTML directly
        // inside <DOCUMENT> without a <TEXT> envelope. Pin the fallback so a
        // refactor that drops the `?? block` half (or reorders the chain)
        // can't silently strip inline-content documents.
        var sgml = """
            <DOCUMENT>
            <TYPE>10-K
            <FILENAME>inline.htm
            <html><body><p>Inline content without TEXT wrapper</p></body></html>
            </DOCUMENT>
            """;

        var result = _sut.Normalize(sgml);

        result.Should().Contain("Inline content without TEXT wrapper");
    }

    [Fact]
    public void Normalize_XbrlWrappedContent_ExtractsFromXbrlTag() {
        var sgml = """
            <DOCUMENT>
            <TYPE>10-K
            <FILENAME>filing.htm
            <TEXT>
            <XBRL>
            <html><body><p>XBRL Content</p></body></html>
            </XBRL>
            </TEXT>
            </DOCUMENT>
            """;

        var result = _sut.Normalize(sgml);

        result.Should().Contain("XBRL Content");
    }
}
