namespace Equibles.Holdings.Repositories.Models;

public class ScreenerRow
{
    public Guid CommonStockId { get; set; }

    public string Ticker { get; set; }

    public string Name { get; set; }

    public Guid? IndustryId { get; set; }

    public string IndustryName { get; set; }

    public long SharesOutStanding { get; set; }

    public int CurrentFilerCount { get; set; }

    public int PreviousFilerCount { get; set; }

    public int DeltaFilerCount => CurrentFilerCount - PreviousFilerCount;

    public long CurrentValue { get; set; }

    public long PreviousValue { get; set; }

    public long DeltaValue => CurrentValue - PreviousValue;

    public long CurrentShares { get; set; }

    public int NewFilerCount { get; set; }

    public int SoldOutFilerCount { get; set; }

    // Percent of float held by 13F filers, derived in-controller after materialization.
    // Repository projection cannot compute it without a translatable conditional divide,
    // and the value is null whenever SharesOutStanding == 0 (unknown).
    public double? PercentOfFloat { get; set; }
}
