using Equibles.Search.Abstractions;

namespace Equibles.Web.ViewModels.Search;

public class GlobalSearchViewModel
{
    public string Query { get; set; }

    /// <summary>Non-empty groups, already ordered by the aggregator.</summary>
    public List<SearchResultGroup> Groups { get; set; } = [];

    /// <summary>Selected category filter; null means "all".</summary>
    public string ActiveCategory { get; set; }

    /// <summary>Selected hit ordering.</summary>
    public SearchSort SortBy { get; set; } = SearchSort.Relevance;

    public bool HasResults => Groups.Count > 0;
}
