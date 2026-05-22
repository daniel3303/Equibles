namespace Equibles.Web.ViewModels.Institutions;

public class InstitutionListItemViewModel
{
    public Guid Id { get; set; }
    public string Cik { get; set; }
    public string Name { get; set; }
    public string City { get; set; }
    public string StateOrCountry { get; set; }

    // Per-filer aggregates at each filer's own most-recent 13F report. Filers
    // that have stopped filing keep their historical aggregate from their last
    // report instead of dropping to zero just because the universe has moved
    // on. Null when the filer has never reported a 13F.
    public int PositionCount { get; set; }
    public long TotalValue { get; set; }
    public DateOnly? LatestReportDate { get; set; }
}
