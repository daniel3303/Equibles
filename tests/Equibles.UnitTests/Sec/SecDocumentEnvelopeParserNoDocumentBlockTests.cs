using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentEnvelopeParserNoDocumentBlockTests
{
    [Fact]
    public void TryExtractPaperPdfFilename_HeaderOnlyEnvelopeWithNoDocumentBlock_ReturnsFalseInsteadOfLoopingOrThrowing()
    {
        // TryExtractPaperPdfFilename loops `while (pos < envelope.Length)`,
        // breaking only when `envelope.IndexOf(DocumentStartTag, pos, ...) == -1
        // → return false` (SecDocumentEnvelopeParser.cs:30-31). That guard is the
        // ONLY exit path for a header-only envelope (SEC sometimes returns
        // `<SEC-HEADER>...</SEC-HEADER>` with no document blocks when a filing's
        // index points to an archived/withdrawn submission, or when the upstream
        // CDN serves the metadata stub rather than the full envelope). A refactor
        // that "simplifies" the loop to `while (true)` (under the false intuition
        // that `pos < envelope.Length` already terminates), or that swaps the
        // `return false` for `break;` followed by code that assumes a block was
        // found, would compile and pass every existing complete-envelope test,
        // then either hang the scraper or throw inside the next `Substring` call
        // — turning a graceful "no paper PDF here" into a worker outage. Pin the
        // exact contract: the non-empty header-only envelope returns false
        // without throwing.
        var envelope = """
            <SEC-DOCUMENT>
            <SEC-HEADER>
            ACCESSION-NUMBER: 0001234567-25-000001
            CONFORMED-SUBMISSION-TYPE: 6-K
            </SEC-HEADER>
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
