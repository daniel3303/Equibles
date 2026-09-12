namespace Equibles.CommonStocks.BusinessLogic.Directory;

// Source readers validate the captured directory/product relationship before constructing this input.
public class EquityDirectoryListingInput
{
    public string Source { get; set; }
    public string SourceIssuerIdentifier { get; set; }
    public string IssuerName { get; set; }
    public string LegalEntityIdentifier { get; set; }
    public List<string> RelatedIsins { get; set; } = [];
    public string Isin { get; set; }
    public string Ticker { get; set; }
    public string MarketIdentifierCode { get; set; }
    public string MarketCountryCode { get; set; }
    public string TradingCurrency { get; set; }
    public decimal? QuoteUnitMultiplier { get; set; }
    public string SourceUrl { get; set; }
    public string PayloadJson { get; set; }
}
