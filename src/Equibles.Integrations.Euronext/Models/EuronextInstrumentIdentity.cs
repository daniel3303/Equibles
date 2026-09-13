namespace Equibles.Integrations.Euronext.Models;

public class EuronextInstrumentIdentity
{
    public string Isin { get; set; }
    public string Symbol { get; set; }
    public string MarketIdentifierCode { get; set; }
    public string Name { get; set; }
    public string IssuerCode { get; set; }
    public string SourceInstrumentType { get; set; }
    public Uri SourceUrl { get; set; }
    public string RawInstrumentJson { get; set; }
}
