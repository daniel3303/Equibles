namespace Equibles.EquityMarkets.BusinessLogic.Directory;

// The source's own confirmation of one row (a product page, or the row itself when the file is the product).
public sealed class EquityMarketDirectoryProduct
{
    public string SourceIssuerIdentifier { get; set; }
    public string Name { get; set; }
    public Uri SourceUrl { get; set; }
    public string ReportedCurrency { get; set; }
    public object Evidence { get; set; }
}
