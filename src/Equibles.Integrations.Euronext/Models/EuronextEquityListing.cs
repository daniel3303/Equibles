namespace Equibles.Integrations.Euronext.Models;

public class EuronextEquityListing
{
    public string Name { get; set; }
    public string Isin { get; set; }
    public string Symbol { get; set; }
    public string MarketIdentifierCode { get; set; }
    public string ReportedCurrency { get; set; }
    public Uri SourceUrl { get; set; }
}
