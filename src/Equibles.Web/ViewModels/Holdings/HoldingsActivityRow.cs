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
}
