namespace Equibles.Web.ViewModels.Holdings;

public class ScreenerResultRow
{
    public Guid CommonStockId { get; set; }

    public string Ticker { get; set; }

    public string Name { get; set; }

    public string IndustryName { get; set; }

    public int CurrentFilerCount { get; set; }

    public int PreviousFilerCount { get; set; }

    public int DeltaFilerCount { get; set; }

    public long CurrentValue { get; set; }

    public long PreviousValue { get; set; }

    public long DeltaValue { get; set; }

    public int NewFilerCount { get; set; }

    public int SoldOutFilerCount { get; set; }

    public double? PercentOfFloat { get; set; }
}
