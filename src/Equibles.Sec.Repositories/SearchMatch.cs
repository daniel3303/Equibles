namespace Equibles.Sec.Repositories;

// How a filing search treats its query text.
public enum SearchMatch
{
    // Relevance-ranked retrieval over the query's words.
    Ranked,

    // Only excerpts containing the query as one exact phrase, ranked by relevance.
    Phrase,
}
