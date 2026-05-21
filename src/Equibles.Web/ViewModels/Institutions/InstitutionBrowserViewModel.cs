namespace Equibles.Web.ViewModels.Institutions;

public class InstitutionBrowserViewModel
{
    public List<InstitutionListItemViewModel> Institutions { get; set; } = [];
    public string Search { get; set; }
    public InstitutionSort Sort { get; set; } = InstitutionSort.Name;

    // Latest universe-wide 13F report date — drives the per-filer aggregates
    // and the page subtitle. Null when no holdings have been ingested yet.
    public DateOnly? LatestReportDate { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalCount { get; set; }
    public int TotalPages => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);
}
