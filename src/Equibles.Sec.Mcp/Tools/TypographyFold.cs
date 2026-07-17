namespace Equibles.Sec.Mcp.Tools;

/// <summary>
/// Folds typographic punctuation to its ASCII equivalent for text matching. Stored SEC
/// filings and transcripts carry smart punctuation (U+2019 apostrophes, curly quotes,
/// en/em dashes) while tool callers type ASCII, so an ordinal substring search without
/// this fold silently misses text the document visibly contains.
///
/// The mapping is strictly one-to-one per char — folding never changes string length —
/// so an index found in a folded string addresses the same characters in the original.
/// Callers rely on that to highlight matches in the original text.
/// </summary>
public static class TypographyFold
{
    public static string Fold(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Common case: nothing to fold — skip the allocation.
        var needsFold = false;
        foreach (var c in value)
        {
            if (FoldChar(c) != c)
            {
                needsFold = true;
                break;
            }
        }

        if (!needsFold)
            return value;

        return string.Create(
            value.Length,
            value,
            (span, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                    span[i] = FoldChar(source[i]);
            }
        );
    }

    // Char classes mirror the typography fold used by the extraction lanes' grounding
    // normalizers: single-quote variants to ', double-quote variants to ", dash variants
    // to -, plus the non-breaking space to a plain space. Escapes, not literals, so the
    // mapping is reviewable at a glance.
    private static char FoldChar(char c) =>
        c switch
        {
            // left/right single quote, low-9 quote, reversed-9 quote, prime, backtick, acute
            '\u2018' or '\u2019' or '\u201A' or '\u201B' or '\u2032' or '`' or '\u00B4' => '\'',
            // left/right double quote, low-9 double quote, reversed-9 double quote, double prime
            '\u201C' or '\u201D' or '\u201E' or '\u201F' or '\u2033' => '"',
            // hyphen, non-breaking hyphen, figure dash, en dash, em dash, horizontal bar, minus
            '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2015' or '\u2212' => '-',
            // non-breaking space
            '\u00A0' => ' ',
            _ => c,
        };
}
