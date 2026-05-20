namespace Equibles.Holdings.Repositories.Models;

public class MarketWideStockActivity
{
    public Guid CommonStockId { get; set; }
    public long CurrentShares { get; set; }
    public long PreviousShares { get; set; }
    public long CurrentValue { get; set; }
    public long PreviousValue { get; set; }
    public int CurrentFilerCount { get; set; }
    public int PreviousFilerCount { get; set; }

    public long DeltaShares => CurrentShares - PreviousShares;
    public long DeltaValue => CurrentValue - PreviousValue;
}
