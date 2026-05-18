namespace Equibles.Search.Abstractions;

/// <summary>What the user typed plus how many hits each provider should return.</summary>
public class SearchRequest
{
    public string Query { get; set; }

    /// <summary>Upper bound on hits a single provider returns for the grouped view.</summary>
    public int MaxPerProvider { get; set; } = 5;
}
