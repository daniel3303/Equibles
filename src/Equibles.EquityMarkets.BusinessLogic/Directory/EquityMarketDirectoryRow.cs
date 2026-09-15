namespace Equibles.EquityMarkets.BusinessLogic.Directory;

// One stated listing in a venue's own directory: the only place a ticker may come from.
public sealed class EquityMarketDirectoryRow
{
    public string Isin { get; set; }
    public string MarketIdentifierCode { get; set; }
    public string Symbol { get; set; }
    public string Name { get; set; }
    public string ReportedCurrency { get; set; }

    // The primary market the directory itself states for the row; null when the source publishes none.
    public string StatedPrimaryMarketIdentifierCode { get; set; }
    public Uri SourceUrl { get; set; }
}
