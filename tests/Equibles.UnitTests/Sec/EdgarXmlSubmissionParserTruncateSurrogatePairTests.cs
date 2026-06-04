using Equibles.Sec.HostedService.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// EdgarXmlSubmissionParser.Truncate caps free-text filing fields to their column
/// width — the doc-comment names "a foreign issuer's long ADR class-title description"
/// as the motivating case, exactly where supplementary-plane characters (CJK Extension
/// B, symbols) appear. Those occupy two UTF-16 code units (a surrogate pair); when the
/// cap lands between them, value[..maxLength] orphans the high surrogate, producing a
/// string that corrupts on the PostgreSQL UTF-8 round-trip (lone surrogates are invalid
/// UTF-8) — the very INSERT failure Truncate exists to prevent. The sibling
/// Congress.DisclosureParsingHelper.Truncate already guards this; this pins the same
/// contract for the SEC helper. Oracle derived from the contract, not the body.
/// </summary>
public class EdgarXmlSubmissionParserTruncateSurrogatePairTests
{
    [Fact(Skip = "GH-3408 — Truncate orphans a surrogate pair at the cut boundary")]
    public void Truncate_MaxLengthSplitsSurrogatePair_ResultContainsNoOrphanSurrogate()
    {
        // "🏛" (U+1F3DB) is a surrogate pair; placed so maxLength=10 falls between the
        // high surrogate (index 9) and the low surrogate (index 10).
        var input = new string('A', 9) + "🏛" + "trailing";

        var result = EdgarXmlSubmissionParser.Truncate(input, 10);

        var hasOrphanSurrogate = result
            .Select((c, i) => (c, i))
            .Any(t =>
                (
                    char.IsHighSurrogate(t.c)
                    && (t.i == result.Length - 1 || !char.IsLowSurrogate(result[t.i + 1]))
                )
                || (
                    char.IsLowSurrogate(t.c) && (t.i == 0 || !char.IsHighSurrogate(result[t.i - 1]))
                )
            );

        hasOrphanSurrogate
            .Should()
            .BeFalse(
                "truncation at a surrogate-pair boundary must not orphan a surrogate — it corrupts on the PostgreSQL UTF-8 round-trip"
            );
    }
}
