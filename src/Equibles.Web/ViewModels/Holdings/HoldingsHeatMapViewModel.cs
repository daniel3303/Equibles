namespace Equibles.Web.ViewModels.Holdings;

public class HoldingsHeatMapViewModel
{
    public List<DateOnly> AvailableDates { get; set; } = [];
    public DateOnly SelectedDate { get; set; }
    public DateOnly? PreviousDate { get; set; }
    public List<HeatMapPoint> Points { get; set; } = [];
    public int TotalUniverseFilers { get; set; }

    public const int MaxPoints = 500;
}
