using Equibles.Web.Extensions;

namespace Equibles.Web.ViewModels.Institutions;

public class InstitutionBrowserViewModel
{
    public List<InstitutionListItemViewModel> Institutions { get; set; } = [];
    public string Search { get; set; }
    public InstitutionSort Sort { get; set; } = InstitutionSort.Name;

    // Active location filters. State is an exact match on the dropdown value;
    // City is a case-insensitive substring match. Null/empty means unfiltered.
    public string State { get; set; }
    public string City { get; set; }

    // Distinct state/country codes present in the filer universe, sorted, used
    // to render the location dropdown options.
    public List<string> States { get; set; } = [];

    // Latest universe-wide 13F report date — drives the per-filer aggregates
    // and the page subtitle. Null when no holdings have been ingested yet.
    public DateOnly? LatestReportDate { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalCount { get; set; }
    public int TotalPages => Pagination.PageCount(TotalCount, PageSize);
}
