namespace Equibles.Web.ViewModels.Holdings;

public class HeatMapPoint
{
    public Guid CommonStockId { get; set; }
    public string Ticker { get; set; }
    public string Name { get; set; }
    public int CurrentFilerCount { get; set; }
    public long CurrentValue { get; set; }
    public double ConvictionScore { get; set; }
    public double NetConvictionPct { get; set; }
    public double RetentionPct { get; set; }
    public double UniversePct { get; set; }
}
