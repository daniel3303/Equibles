namespace Equibles.EquityMarkets.BusinessLogic.Directory;

public sealed class EquityMarketDirectoryImportResult
{
    public int Listings { get; set; }
    public int Imported { get; set; }
    public int Current { get; set; }
    public int Skipped { get; set; }
    public int Excluded { get; set; }
    public int Failed { get; set; }
    public string Error { get; set; }
}
