namespace Equibles.Integrations.Wikidata.Models;

/// <summary>
/// The W3C SPARQL 1.1 query-results JSON envelope, reduced to the slice this
/// integration reads (<c>results.bindings[].{var}.value</c>).
/// </summary>
public class SparqlResultsResponse
{
    public SparqlResults Results { get; set; } = new();
}
