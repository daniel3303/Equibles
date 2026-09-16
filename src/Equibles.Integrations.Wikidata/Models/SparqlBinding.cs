namespace Equibles.Integrations.Wikidata.Models;

/// <summary>
/// One result row; property names mirror the variable names selected by the
/// query (<c>?key ?website</c>, where the key is the CIK or LEI the query joined on).
/// </summary>
public class SparqlBinding
{
    public SparqlValue Key { get; set; }
    public SparqlValue Website { get; set; }
}
