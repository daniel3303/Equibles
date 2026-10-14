namespace Equibles.Sec.BusinessLogic.Search;

// What a caller should do after a filing search runs out of its statement budget. A corpus-wide
// search has no bounded fallback, so a narrower call is what works; a scoped one already degraded,
// so a warm retry is the advice there (production, 2026-09-23).
public static class SearchTimeoutAdvice
{
    public const string Unscoped =
        "The search timed out before it could rank the excerpts. Narrow it with a ticker, or shorten the query to its 3-4 most specific terms, then retry.";

    public const string Scoped =
        "The search timed out before it could rank the excerpts. Retry the same call, or shorten the query to its 3-4 most specific terms.";

    public static string For(bool scoped) => scoped ? Scoped : Unscoped;
}
