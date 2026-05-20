using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentEnvelopeParserUnicodeFilenameTests
{
    // The WHY-comment on IsSafeFilename documents the allowlist exactly:
    // "EDGAR filenames are always bare names ([A-Za-z0-9._-]+)". The
    // implementation enforces this via `char.IsAsciiLetterOrDigit(ch)
    // || ch == '.' || ch == '_' || ch == '-'`. The existing pins cover
    // `..` traversal, `\` separator, `%` URL-encoded traversal, leading
    // `.`, and the happy path — but none isolates the ASCII restriction
    // itself. A one-keyword refactor from `IsAsciiLetterOrDigit` to
    // `IsLetterOrDigit` (Unicode-aware) would compile cleanly, pass
    // every existing test (none feed non-ASCII), and silently admit
    // Unicode-bearing filenames into the EDGAR URL composition. Even
    // when downstream URL escaping is correct, the broader allowlist
    // weakens the defence-in-depth the WHY-comment explicitly relies
    // on. Pin a filename whose only disallowed character is a
    // non-ASCII letter — leading char is ASCII (passes leading-`.`),
    // no `/` `\` or `%` (passes traversal guards), ends in `.pdf`
    // (passes EndsWith) — so only IsBareNameChar's ASCII check fires.
    [Fact]
    public void TryExtractPaperPdfFilename_FilenameWithNonAsciiLetter_RejectsAndReturnsFalse()
    {
        var envelope = """
            <SEC-DOCUMENT>
            <SEC-HEADER>
            </SEC-HEADER>
            <DOCUMENT>
            <TYPE>6-K
            <SEQUENCE>1
            <FILENAME>café.pdf
            <DESCRIPTION>Form 6-K
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
