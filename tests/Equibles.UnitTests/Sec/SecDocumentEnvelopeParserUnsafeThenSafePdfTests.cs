using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentEnvelopeParserUnsafeThenSafePdfTests
{
    [Fact]
    public void TryExtractPaperPdfFilename_UnsafePdfInFirstBlockThenSafePdfInSecond_ReturnsSafeFilename()
    {
        // A traversal `.pdf` in an earlier DOCUMENT block must `continue` the scan,
        // not abort it — otherwise an injected first-block filename suppresses a
        // legitimate paper PDF (and the safe later block must still be returned).
        var envelope = """
            <SEC-DOCUMENT>
            <SEC-HEADER>
            </SEC-HEADER>
            <DOCUMENT>
            <TYPE>EX-99
            <SEQUENCE>1
            <FILENAME>../../etc/passwd.pdf
            <DESCRIPTION>Hostile attachment
            <TEXT>
            </TEXT>
            </DOCUMENT>
            <DOCUMENT>
            <TYPE>6-K
            <SEQUENCE>2
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

        var success = SecDocumentEnvelopeParser.TryExtractPaperPdfFilename(
            envelope,
            out var filename
        );

        success.Should().BeTrue();
        filename.Should().Be("form6k.pdf");
    }
}
