using Equibles.Holdings.Data.Models;
using Equibles.Web.ViewModels.Shared;

namespace Equibles.Web.ViewModels.Institutions;

public class InstitutionBrowserViewModel : PagedBrowserViewModel
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

    // Range filters on each filer's most-recent 13F aggregate. MinValue/MaxValue
    // bound the total book size in dollars; MinPositions/MaxPositions bound the
    // holding count. Null means that bound is unset.
    public long? MinValue { get; set; }
    public long? MaxValue { get; set; }
    public int? MinPositions { get; set; }
    public int? MaxPositions { get; set; }

    // Active SEC filing-type filter (13F vs Schedule 13D/13G). Null means all
    // types; when set, the listed filers and their aggregates are scoped to it.
    public FilingType? FilingType { get; set; }

    // Latest universe-wide 13F report date — drives the per-filer aggregates
    // and the page subtitle. Null when no holdings have been ingested yet.
    public DateOnly? LatestReportDate { get; set; }
}
