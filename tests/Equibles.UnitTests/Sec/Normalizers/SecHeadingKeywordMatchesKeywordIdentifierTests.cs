using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class SecHeadingKeywordMatchesKeywordIdentifierTests
{
    // Contract (from the doc-comment): MatchesKeywordIdentifier tells a real "Part" header
    // apart from prose that merely starts with the keyword, and EDGAR renders the separator
    // between keyword and identifier as a non-breaking space (U+00A0) — "any Unicode
    // whitespace counts". This pins both halves of that discriminator on the shared helper:
    // the nbsp-separated header is recognised (guards against an ASCII-only `== ' '` check),
    // while a keyword-prefixed word ("Partnership") has no whitespace boundary and is rejected.
    [Fact]
    public void MatchesKeywordIdentifier_NonBreakingSpaceSeparator_RecognisesHeaderNotProse()
    {
        var nbspHeader = SecHeadingKeyword.MatchesKeywordIdentifier(
            "Part IV",
            "PART",
            SecHeadingKeyword.IsRomanNumeral
        );
        var prefixedProse = SecHeadingKeyword.MatchesKeywordIdentifier(
            "Partnership",
            "PART",
            SecHeadingKeyword.IsRomanNumeral
        );

        nbspHeader
            .Should()
            .BeTrue("EDGAR separates 'Part' from its numeral with a non-breaking space (U+00A0)");
        prefixedProse
            .Should()
            .BeFalse("'Partnership' has no whitespace boundary after 'Part' and is not a header");
    }
}
