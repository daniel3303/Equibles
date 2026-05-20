namespace Equibles.Web.ViewModels.Holdings;

public class ScreenerViewModel
{
    public ScreenerCriteriaViewModel Filters { get; set; } = new();

    public List<DateOnly> AvailableDates { get; set; } = [];

    public DateOnly SelectedDate { get; set; }

    public DateOnly ComparisonDate { get; set; }

    public List<ScreenerIndustryOption> IndustryOptions { get; set; } = [];

    public List<ScreenerResultRow> Rows { get; set; } = [];

    public bool TruncatedToCap { get; set; }

    public string Reason { get; set; }
}
