using System.Globalization;

namespace Equibles.Sec.BusinessLogic.Search;

/// <summary>
/// Shortens a long keyword query to its few most specific terms for a last BM25 pass after the
/// full-length passes ran out of budget. Fewer terms read far fewer posting lists (one production
/// run: 4.7s for an eight-term query against 0.19s for three of its terms).
/// This relaxes a search query only; it never classifies data.
/// </summary>
internal static class Bm25QueryReducer
{
    public const int MaxTerms = 4;

    // Query-string operators are dropped with the stop words: a kept NOT would flip the meaning
    // of the shortened query, and a kept AND/OR/TO would spend a term slot on syntax.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "an",
        "and",
        "are",
        "as",
        "at",
        "be",
        "by",
        "for",
        "from",
        "has",
        "have",
        "in",
        "into",
        "is",
        "it",
        "its",
        "not",
        "of",
        "on",
        "or",
        "that",
        "the",
        "their",
        "this",
        "to",
        "vs",
        "was",
        "were",
        "what",
        "which",
        "with",
    };

    /// <summary>
    /// Returns the reduced query, or null when the query already has <see cref="MaxTerms"/> or
    /// fewer terms and a shorter pass would repeat the one that timed out.
    /// </summary>
    public static string Reduce(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        // Every character outside a word becomes a separator, so quotes, parentheses, field:value
        // prefixes and +/- modifiers cannot reach the parser; inner '-' and '.' stay (PV-10, U.S.).
        var words = new string(
            query
                .Select(c =>
                    char.IsLetterOrDigit(c)
                    || c is '-' or '.'
                    || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark
                        ? c
                        : ' '
                )
                .ToArray()
        );
        var terms = words
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(term => term.Trim('-', '.'))
            .Where(term => term.Length > 1 && !StopWords.Contains(term))
            .DistinctBy(term => term, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (terms.Count <= MaxTerms)
            return null;

        // Names, acronyms and figures (Alberta, PDP, PV-10, 2026) narrow a search more than
        // generic words; among the rest a longer word is usually the rarer one. Kept terms stay
        // in the caller's order so the shortened query still reads like the original.
        return string.Join(
            ' ',
            terms
                .Select((term, position) => (term, position))
                .OrderByDescending(entry => IsSpecific(entry.term))
                .ThenByDescending(entry => entry.term.Length)
                .ThenBy(entry => entry.position)
                .Take(MaxTerms)
                .OrderBy(entry => entry.position)
                .Select(entry => entry.term)
        );
    }

    private static bool IsSpecific(string term) => term.Any(char.IsDigit) || term.Any(char.IsUpper);
}
