using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentEnvelopeParserMissingFilenameTagTests
{
    // Contract: TryExtractPaperPdfFilename must tolerate a DOCUMENT block that
    // has no <FILENAME> tag (TryExtractSgmlTagValue returns false → block
    // skipped). A regression that dropped the IndexOf == -1 guard would throw
    // ArgumentOutOfRangeException on the next `Substring(valueStart, …)` call
    // and crash the document scraper for any filing whose first block omits
    // FILENAME (e.g. some legacy <PAPER> envelopes with TYPE/SEQUENCE/TEXT but
    // no explicit filename).
    [Fact]
    public void TryExtractPaperPdfFilename_DocumentBlockMissingFilenameTag_ReturnsFalse()
    {
        var envelope = """
            <SEC-DOCUMENT>
            <DOCUMENT>
            <TYPE>6-K
            <SEQUENCE>1
            <TEXT>
            </TEXT>
            </DOCUMENT>
            </SEC-DOCUMENT>
            """;

        var success = SecDocumentEnvelopeParser.TryExtractPaperPdfFilename(
            envelope,
            out var filename
        );

        success.Should().BeFalse();
        filename.Should().BeEmpty();
    }
}
