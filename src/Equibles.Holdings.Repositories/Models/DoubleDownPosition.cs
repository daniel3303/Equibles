namespace Equibles.Holdings.Repositories.Models;

public class DoubleDownPosition
{
    public Guid InstitutionalHolderId { get; set; }
    public string FilerName { get; set; }
    public string FilerCik { get; set; }
    public Guid CommonStockId { get; set; }
    public string Ticker { get; set; }
    public string StockName { get; set; }
    public long CurrentShares { get; set; }
    public long PreviousShares { get; set; }
    public long CurrentValue { get; set; }
    public long PreviousValue { get; set; }

    public long DeltaShares => CurrentShares - PreviousShares;
    public double PctChange =>
        PreviousShares > 0 ? (double)DeltaShares / PreviousShares * 100.0 : 0;
}
