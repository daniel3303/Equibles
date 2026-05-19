namespace Equibles.Search.Abstractions;

/// <summary>What the user typed plus how many hits each provider should return.</summary>
public class SearchRequest
{
    public string Query { get; set; }

    /// <summary>Upper bound on hits a single provider returns for the grouped view.</summary>
    public int MaxPerProvider { get; set; } = 5;

    /// <summary>How hits are ordered within each group; applied by the aggregator, not providers.</summary>
    public SearchSort SortBy { get; set; } = SearchSort.Relevance;

    /// <summary>Inclusive lower bound on a hit's date; only providers with a date dimension honor it.</summary>
    public DateOnly? DateFrom { get; set; }

    /// <summary>Inclusive upper bound on a hit's date; only providers with a date dimension honor it.</summary>
    public DateOnly? DateTo { get; set; }
}
