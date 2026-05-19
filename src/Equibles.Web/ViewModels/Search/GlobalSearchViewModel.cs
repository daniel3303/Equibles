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

    /// <summary>Inclusive date-range filter (honored by date-aware providers, e.g. SEC Filings).</summary>
    public DateOnly? DateFrom { get; set; }

    public DateOnly? DateTo { get; set; }

    public bool HasResults => Groups.Count > 0;
}
