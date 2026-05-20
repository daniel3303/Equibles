namespace Equibles.Web.ViewModels.Holdings;

public class HoldingsActivityViewModel
{
    public List<DateOnly> AvailableDates { get; set; } = [];
    public DateOnly SelectedDate { get; set; }
    public DateOnly? PreviousDate { get; set; }

    public List<HoldingsActivityRow> TopBuys { get; set; } = [];
    public List<HoldingsActivityRow> TopSells { get; set; } = [];

    public const int RowCap = 20;
}
