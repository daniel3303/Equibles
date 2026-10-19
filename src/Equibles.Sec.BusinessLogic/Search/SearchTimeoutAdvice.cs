using Equibles.Sec.Repositories;

namespace Equibles.Sec.BusinessLogic.Search;

// What a caller should do after a filing search runs out of its statement budget. A corpus-wide
// search has no bounded fallback, so a narrower call is what works; a scoped one already degraded,
// so a warm retry is the advice there (production, 2026-09-23). Shortening is offered only when
// the query has more terms than the shortened-query pass keeps, since otherwise it changes nothing.
public static class SearchTimeoutAdvice
{
    public const string Unscoped =
        "The search timed out before it could rank the excerpts. Narrow it with a ticker or a date window, then retry.";

    public const string UnscopedLongQuery =
        "The search timed out before it could rank the excerpts. Narrow it with a ticker, or shorten the query to its 3-4 most specific terms, then retry.";

    public const string Scoped =
        "The search timed out before it could rank the excerpts. Retry the same call, or narrow it with a date window or a document type.";

    public const string ScopedLongQuery =
        "The search timed out before it could rank the excerpts. Retry the same call, or shorten the query to its 3-4 most specific terms.";

    // Shortening a phrase would search for a different phrase, so the advice narrows the scope.
    public const string UnscopedPhrase =
        "The phrase search timed out before it finished. Narrow it with a ticker or a date window, then retry.";

    public const string ScopedPhrase =
        "The phrase search timed out before it finished. Retry the same call, or narrow it with a date window.";

    public static string For(
        bool scoped,
        SearchMatch match = SearchMatch.Ranked,
        string query = null
    )
    {
        if (match == SearchMatch.Phrase)
            return scoped ? ScopedPhrase : UnscopedPhrase;
        var shortenable = Bm25QueryReducer.Reduce(query) != null;
        if (scoped)
            return shortenable ? ScopedLongQuery : Scoped;
        return shortenable ? UnscopedLongQuery : Unscoped;
    }
}
