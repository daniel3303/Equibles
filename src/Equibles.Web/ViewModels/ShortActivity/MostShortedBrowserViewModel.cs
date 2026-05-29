using Equibles.Web.Extensions;

namespace Equibles.Web.ViewModels.ShortActivity;

public class MostShortedBrowserViewModel
{
    public List<MostShortedListItemViewModel> Records { get; set; } = [];

    // The settlement date being shown (the parsed selection, or the latest available).
    public DateOnly? SelectedDate { get; set; }

    // The most recent settlement date on file — drives the default selection and the subtitle.
    public DateOnly? LatestDate { get; set; }

    // Distinct FINRA bi-monthly settlement dates, newest first — populates the date selector.
    public List<DateOnly> AvailableDates { get; set; } = [];

    public MostShortedSort Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalCount { get; set; }
    public int TotalPages => Pagination.PageCount(TotalCount, PageSize);
}
