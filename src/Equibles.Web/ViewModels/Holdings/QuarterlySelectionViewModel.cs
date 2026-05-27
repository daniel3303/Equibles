namespace Equibles.Web.ViewModels.Holdings;

public abstract class QuarterlySelectionViewModel
{
    public List<DateOnly> AvailableDates { get; set; } = [];
    public DateOnly SelectedDate { get; set; }
    public DateOnly? PreviousDate { get; set; }
    public bool IsCombinedAvailable { get; set; }
    public bool IsCombinedSelected { get; set; }
}
