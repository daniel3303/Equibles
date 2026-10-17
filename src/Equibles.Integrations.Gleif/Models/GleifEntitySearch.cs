namespace Equibles.Integrations.Gleif.Models;

public class GleifEntitySearch
{
    public string Query { get; set; }
    public Uri SourceUrl { get; set; }

    // GLEIF's reported match count; Entities holds at most the requested page of them, in GLEIF's order, which
    // promises no relevance ranking.
    public int Total { get; set; }
    public List<GleifEntity> Entities { get; set; } = [];
}
