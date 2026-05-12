using Equibles.Sec.BusinessLogic;

namespace Equibles.Tests.Sec;

public class SecDocumentEnvelopeParserTests {
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
