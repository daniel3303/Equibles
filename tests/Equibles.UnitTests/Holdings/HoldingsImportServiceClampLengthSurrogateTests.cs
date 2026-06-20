using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// HoldingsImportService.ClampLength caps a parsed 13F cover-page field to its column
/// width — the doc-comment names the cover page as "free text" and says the cap exists so
/// "storing the prefix beats losing the filer's whole batch to a 22001 abort". The widest
/// field it guards is CompanyName (512). Free text can carry supplementary-plane characters
/// (CJK Extension B, symbols), each two UTF-16 code units (a surrogate pair); when the cap
/// lands between them, value[..maxLength] orphans the high surrogate, producing a string
/// that corrupts on the PostgreSQL UTF-8 round-trip (lone surrogates are invalid UTF-8) —
/// the very 22001 INSERT failure ClampLength exists to prevent. The sibling
/// EdgarXmlSubmissionParser.Truncate already guards this; this pins the same contract.
/// Oracle derived from the contract, not the body.
/// </summary>
public class HoldingsImportServiceClampLengthSurrogateTests
{
    [Fact(Skip = "GH-3849 — ClampLength orphans a surrogate pair at the column-width cap")]
    public void ClampLength_MaxLengthSplitsSurrogatePair_ResultContainsNoOrphanSurrogate()
    {
        // "🏛" (U+1F3DB) is a surrogate pair; placed so maxLength=10 falls between the
        // high surrogate (index 9) and the low surrogate (index 10).
        var input = new string('A', 9) + "🏛" + "trailing";

        var result = HoldingsImportService.ClampLength(input, 10);

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
                "truncation at a surrogate-pair boundary must not orphan a surrogate — it corrupts on the PostgreSQL UTF-8 round-trip, the 22001 abort ClampLength exists to prevent"
            );
    }
}
