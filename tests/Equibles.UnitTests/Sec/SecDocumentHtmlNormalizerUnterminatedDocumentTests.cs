using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentHtmlNormalizerUnterminatedDocumentTests
{
    private readonly SecDocumentHtmlNormalizer _sut = new();

    // Contract: SEC submissions are framed as <DOCUMENT>…</DOCUMENT> blocks. A
    // truncated submission whose block opens but is never closed must be skipped
    // safely — the extractor scans for the closing tag and, not finding it, must
    // stop without slicing past the buffer. A naive substring from the open tag
    // without the missing-close guard would throw; the contract is empty output,
    // no exception.
    [Fact]
    public void Normalize_DocumentBlockWithoutClosingTag_ReturnsEmptyWithoutThrowing()
    {
        var sgml = """
            <DOCUMENT>
            <TYPE>10-K
            <FILENAME>filing.htm
            <TEXT>
            <html><body><p>Truncated submission</p></body></html>
            """;

        var result = _sut.Normalize(sgml);

        result.Should().BeEmpty();
    }
}
