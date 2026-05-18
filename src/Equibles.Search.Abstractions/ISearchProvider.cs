namespace Equibles.Search.Abstractions;

/// <summary>
/// A single searchable domain. Each module contributes one implementation living in its own
/// package; the search service discovers them by interface and never references the module.
/// Adding a module therefore never edits the aggregator (Open/Closed).
/// </summary>
public interface ISearchProvider
{
    /// <summary>Display name of the result group, e.g. "Stocks", "SEC Filings".</summary>
    string Category { get; }

    /// <summary>Display order of this group on the results page (ascending).</summary>
    int Order { get; }

    /// <summary>
    /// Returns the matches for <paramref name="request"/>. Implementations must honour
    /// <see cref="SearchRequest.MaxPerProvider"/> and the cancellation token; the aggregator
    /// isolates failures, so throwing here degrades only this group, not the whole page.
    /// </summary>
    Task<SearchResultGroup> Search(SearchRequest request, CancellationToken cancellationToken);
}
