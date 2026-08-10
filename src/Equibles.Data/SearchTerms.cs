using System.Text;

namespace Equibles.Data;

/// <summary>
/// Normalizes caller-written discovery queries into punctuation-independent tokens.
/// Repositories apply every returned token, so word order and stored punctuation do not
/// decide whether a natural-language name matches.
/// </summary>
public static class SearchTerms
{
    public static IReadOnlyList<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var token = new StringBuilder();

        void Flush()
        {
            if (token.Length == 0)
                return;

            var value = token.ToString();
            if (seen.Add(value))
                tokens.Add(value);
            token.Clear();
        }

        foreach (var character in text.Trim())
        {
            if (char.IsLetterOrDigit(character))
                token.Append(char.ToLowerInvariant(character));
            else
                Flush();
        }

        Flush();
        return tokens;
    }

    public static string Normalize(string text) => string.Join(' ', Tokenize(text));

    /// <summary>
    /// Returns the strict all-token/alias query when it has any rows; otherwise returns the
    /// broader any-token query. The existence check stays inside the provider expression, so
    /// callers retain one composable query for counts, ordering, and paging.
    /// </summary>
    public static IQueryable<T> WithSparseAnyTokenFallback<T>(
        IQueryable<T> strict,
        IQueryable<T> anyToken
    ) => strict.Concat(anyToken.Where(_ => !strict.Any())).Distinct();

    /// <summary>
    /// Returns the first non-empty resolution tier: exact stored identifier, verified alias,
    /// strict all-token match, then sparse any-token fallback. The existence checks remain in
    /// the provider expression so callers retain one composable query.
    /// </summary>
    public static IQueryable<T> WithExclusiveResolutionTiers<T>(
        IQueryable<T> exactIdentifier,
        IQueryable<T> verifiedAlias,
        IQueryable<T> strict,
        IQueryable<T> anyToken
    ) =>
        exactIdentifier
            .Concat(verifiedAlias.Where(_ => !exactIdentifier.Any()))
            .Concat(strict.Where(_ => !exactIdentifier.Any() && !verifiedAlias.Any()))
            .Concat(
                anyToken.Where(_ => !exactIdentifier.Any() && !verifiedAlias.Any() && !strict.Any())
            )
            .Distinct();
}
