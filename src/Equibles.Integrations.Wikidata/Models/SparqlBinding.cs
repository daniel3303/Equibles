namespace Equibles.Integrations.Wikidata.Models;

/// <summary>
/// One result row; property names mirror the variable names selected by the
/// query (<c>?cik ?website</c>).
/// </summary>
public class SparqlBinding
{
    public SparqlValue Cik { get; set; }
    public SparqlValue Website { get; set; }
}
