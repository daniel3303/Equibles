namespace Equibles.Web.ViewModels.Holdings;

public class HoldingsActivityRow
{
    public Guid CommonStockId { get; set; }
    public string Ticker { get; set; }
    public string Name { get; set; }
    public long DeltaShares { get; set; }
    public long DeltaValue { get; set; }
    public int CurrentFilerCount { get; set; }
    public int PreviousFilerCount { get; set; }

    // Only populated for the New / Sold-out boards; left zero on Top Buys / Top Sells rows
    // where the value-delta ranking carries the signal.
    public int NewFilerCount { get; set; }
    public int SoldOutFilerCount { get; set; }
}
