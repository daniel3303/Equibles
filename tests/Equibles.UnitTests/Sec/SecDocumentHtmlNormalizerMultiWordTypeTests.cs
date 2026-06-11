using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentHtmlNormalizerMultiWordTypeTests
{
    private readonly SecDocumentHtmlNormalizer _sut = new();

    [Fact]
    public void Normalize_MultiWordFormType_IsAllowed()
    {
        // "DEF 14A" contains a space, so a first-token read of the TYPE line
        // yields "DEF" and the proxy is silently dropped from the pipeline.
        // The normalizer must match the multi-word display name.
        var sgml = """
            <DOCUMENT>
            <TYPE>DEF 14A
            <FILENAME>proxy.htm
            <TEXT>
            <html><body><p>Proxy statement body</p></body></html>
            </TEXT>
            </DOCUMENT>
            """;

        var result = _sut.Normalize(sgml);

        result.Should().Contain("Proxy statement body");
    }

    [Fact]
    public void Normalize_SingleWordTypeWithDescriptiveTrailer_IsStillAllowed()
    {
        // SEC sometimes appends a descriptive trailer after the bare form name
        // ("10-K   Annual Report"). The longest-prefix match must fall back to
        // the bare token so trailers keep working.
        var sgml = """
            <DOCUMENT>
            <TYPE>10-K   Annual Report
            <FILENAME>annual.htm
            <TEXT>
            <html><body><p>Annual report body</p></body></html>
            </TEXT>
            </DOCUMENT>
            """;

        var result = _sut.Normalize(sgml);

        result.Should().Contain("Annual report body");
    }

    [Fact]
    public void Normalize_UnknownMultiWordType_IsFiltered()
    {
        // No token prefix of "PRE 14A" is a registered display name (preliminary
        // proxies are deliberately not collected) and it is not an exhibit, so
        // the document block must stay filtered out.
        var sgml = """
            <DOCUMENT>
            <TYPE>PRE 14A
            <FILENAME>prelim.htm
            <TEXT>
            <html><body><p>Preliminary proxy body</p></body></html>
            </TEXT>
            </DOCUMENT>
            """;

        var result = _sut.Normalize(sgml);

        result.Should().BeEmpty();
    }
}
