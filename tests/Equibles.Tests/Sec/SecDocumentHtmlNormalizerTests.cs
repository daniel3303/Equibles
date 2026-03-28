using Equibles.Sec.BusinessLogic;

namespace Equibles.Tests.Sec;

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
