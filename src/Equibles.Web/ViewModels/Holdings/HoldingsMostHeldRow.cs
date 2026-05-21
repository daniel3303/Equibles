namespace Equibles.Web.ViewModels.Holdings;

public class HoldingsMostHeldRow
{
    public Guid CommonStockId { get; set; }
    public string Ticker { get; set; }
    public string Name { get; set; }
    public int CurrentFilerCount { get; set; }
    public int PreviousFilerCount { get; set; }
    public long CurrentValue { get; set; }
    public long PreviousValue { get; set; }

    public int DeltaFilerCount => CurrentFilerCount - PreviousFilerCount;
    public long DeltaValue => CurrentValue - PreviousValue;

    // % of distinct 13F filers reporting on SelectedDate that hold this stock —
    // 0..100, computed against HoldingsMostHeldViewModel.TotalUniverseFilers.
    public double PercentOfUniverse { get; set; }
}
