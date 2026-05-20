namespace Equibles.Holdings.Repositories.Models;

public enum StockPositionChangeType
{
    Initiated = 1,
    Increased = 2,
    Reduced = 3,
    Exited = 4,
    Unchanged = 5,
}

public class StockPositionChange
{
    public Guid CommonStockId { get; set; }
    public string Ticker { get; set; }
    public string Name { get; set; }

    public long CurrentShares { get; set; }
    public long PreviousShares { get; set; }
    public long CurrentValue { get; set; }
    public long PreviousValue { get; set; }

    public StockPositionChangeType ChangeType { get; set; }

    // 0 when the holder's reported AUM for the current quarter is 0.
    public double PercentOfPortfolio { get; set; }

    public long DeltaShares => CurrentShares - PreviousShares;
    public long DeltaValue => CurrentValue - PreviousValue;
}
