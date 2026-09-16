namespace Equibles.Integrations.NasdaqNordic.Models;

// The instrument header the venue publishes for one order book; the exchange label names the list it belongs to.
public sealed class NasdaqNordicInstrument
{
    public string Symbol { get; set; }
    public string CompanyName { get; set; }
    public string Exchange { get; set; }
    public string Segment { get; set; }
    public string MarketStatus { get; set; }
    public string Isin { get; set; }
    public string Currency { get; set; }
    public Uri SourceUrl { get; set; }
}
