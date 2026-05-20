using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentEnvelopeParserUppercaseExtensionTests
{
    // Contract: TryExtractPaperPdfFilename matches the .pdf suffix with
    // StringComparison.OrdinalIgnoreCase, so a legacy SEC paper-filing envelope
    // emitting an uppercase ".PDF" filename must still be extracted. Every
    // existing happy-path pin uses lowercase ".pdf"; the case-insensitivity
    // arm is unpinned. A refactor that swaps the comparison to ordinal /
    // case-sensitive would silently drop every uppercase paper filing on the
    // floor, leaving callers with no PDF artifact to fetch.
    [Fact]
    public void TryExtractPaperPdfFilename_UppercasePdfExtension_MatchesCaseInsensitively()
    {
        var envelope = """
            <SEC-DOCUMENT>
            <DOCUMENT>
            <TYPE>6-K
            <SEQUENCE>1
            <FILENAME>FORM6K.PDF
            <DESCRIPTION>Form 6-K
            <TEXT>
            begin 644 FORM6K.PDF
            (uuencoded body)
            end
            </TEXT>
            </DOCUMENT>
            </SEC-DOCUMENT>
            """;

        var found = SecDocumentEnvelopeParser.TryExtractPaperPdfFilename(
            envelope,
            out var filename
        );

        found.Should().BeTrue();
        filename.Should().Be("FORM6K.PDF");
    }
}
