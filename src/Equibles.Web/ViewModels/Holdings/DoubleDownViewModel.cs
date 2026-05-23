using Equibles.Holdings.Repositories.Models;

namespace Equibles.Web.ViewModels.Holdings;

public class DoubleDownViewModel
{
    public const int PageSize = 100;
    public const double DefaultMinPct = 50.0;

    public List<DateOnly> AvailableDates { get; set; } = [];
    public DateOnly SelectedDate { get; set; }
    public DateOnly? PreviousDate { get; set; }
    public double MinPctIncrease { get; set; } = DefaultMinPct;

    public List<DoubleDownPosition> Positions { get; set; } = [];
    public int Page { get; set; } = 1;
    public int TotalCount { get; set; }
    public int TotalPages => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);
}
