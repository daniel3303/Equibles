namespace Equibles.Integrations.Xetra.Models;

// One row of the all-tradable-instruments file; the adapter decides which rows are stocks, never the parser.
public sealed class XetraInstrument
{
    public string ProductStatus { get; set; }
    public string InstrumentStatus { get; set; }
    public string Name { get; set; }
    public string Isin { get; set; }
    public string Wkn { get; set; }
    public string Mnemonic { get; set; }
    public string MarketIdentifierCode { get; set; }
    public string InstrumentType { get; set; }
    public string Currency { get; set; }
    public string CountryOfIssue { get; set; }
    public string PrimaryMarketIdentifierCode { get; set; }
}
