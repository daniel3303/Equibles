using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentEnvelopeParserWhitespaceFilenameTests
{
    // Pins the whitespace-only FILENAME branch of TryExtractSgmlTagValue (reached
    // via TryExtractPaperPdfFilename). Existing path-traversal / null / multi-doc
    // pins all carry concrete filenames, so the inner Trim() empty-check after
    // the tag is unhit. A real-world variant: a malformed envelope where the
    // SGML FILENAME header is present but blank. The probe must safely return
    // false without inferring a stray PDF filename.
    [Fact]
    public void TryExtractPaperPdfFilename_FilenameTagWithWhitespaceOnlyValue_ReturnsFalse()
    {
        var envelope =
            "<SEC-DOCUMENT>\n<DOCUMENT>\n<TYPE>PAPER\n<FILENAME>   \n</DOCUMENT>\n</SEC-DOCUMENT>";

        var ok = SecDocumentEnvelopeParser.TryExtractPaperPdfFilename(envelope, out var filename);

        ok.Should().BeFalse();
        filename.Should().BeEmpty();
    }
}
