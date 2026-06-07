namespace Equibles.Sec.BusinessLogic.Normalizers;

// Shared detection for SEC "Part"/"Item" section headers. Both HeadingConversionStep
// and PaginationRemovalStep need to tell a real heading ("Part IV", "Item 1A") apart
// from prose that merely starts with the keyword ("Partnership", "Part of …").
internal static class SecHeadingKeyword
{
    // Uppercase Roman-numeral letters; the text is upper-cased before the check.
    private const string RomanNumeralLetters = "IVXLCDM";

    // True when `text` begins with `keyword` (case-insensitive) followed by a whitespace
    // boundary and a first token satisfying `firstTokenMatches`. SEC EDGAR renders the
    // separator with a non-breaking space (U+00A0), so any Unicode whitespace counts.
    public static bool MatchesKeywordIdentifier(
        string text,
        string keyword,
        Func<string, bool> firstTokenMatches
    )
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var upperText = text.ToUpperInvariant().Trim();
        if (
            !upperText.StartsWith(keyword)
            || upperText.Length <= keyword.Length
            || !char.IsWhiteSpace(upperText[keyword.Length])
        )
            return false;

        var after = upperText.Substring(keyword.Length + 1).Trim();
        var tokens = after.Split([' ', '.', '-', ':'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return false;

        return firstTokenMatches(tokens[0]);
    }

    // A real Part identifier is a Roman numeral (Part I–IV); an ordinary word like
    // "of"/"the" from a prose sentence beginning "Part of …" is all-letters too, so
    // require every character to be a Roman-numeral letter.
    public static bool IsRomanNumeral(string token) =>
        token.All(c => RomanNumeralLetters.Contains(c));

    // Whitespace-delimited word count, used to keep a short bare header apart from a prose
    // sentence that merely opens with the same keyword + identifier. SEC EDGAR's non-breaking
    // space (U+00A0) counts as a separator like any other Unicode whitespace.
    public static int WordCount(string text) =>
        text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
}
