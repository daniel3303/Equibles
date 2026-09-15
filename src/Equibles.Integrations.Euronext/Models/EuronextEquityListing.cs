namespace Equibles.Integrations.Euronext.Models;

public class EuronextEquityListing
{
    public string Name { get; set; }
    public string Isin { get; set; }
    public string Symbol { get; set; }
    public string MarketIdentifierCode { get; set; }

    // The venue whose product page the directory links: this market's own venue, or the sibling market Euronext
    // homes a cross-listed line on.
    public string PrimaryMarketIdentifierCode { get; set; }
    public string ReportedCurrency { get; set; }
    public Uri SourceUrl { get; set; }
}
