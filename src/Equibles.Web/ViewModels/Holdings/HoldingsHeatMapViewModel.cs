namespace Equibles.Web.ViewModels.Holdings;

public class HoldingsHeatMapViewModel : QuarterlySelectionViewModel
{
    public List<HeatMapPoint> Points { get; set; } = [];
    public int TotalUniverseFilers { get; set; }

    public const int MaxPoints = 500;
}
