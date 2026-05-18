namespace Equibles.Search.Abstractions;

/// <summary>The hits one provider found, plus how to title and order the group.</summary>
public class SearchResultGroup
{
    public string Category { get; set; }

    public int Order { get; set; }

    public List<SearchHit> Hits { get; set; } = [];
}
