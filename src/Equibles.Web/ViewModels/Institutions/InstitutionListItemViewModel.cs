namespace Equibles.Web.ViewModels.Institutions;

public class InstitutionListItemViewModel
{
    public Guid Id { get; set; }
    public string Cik { get; set; }
    public string Name { get; set; }
    public string City { get; set; }
    public string StateOrCountry { get; set; }

    // Per-filer aggregates at the universe's latest 13F report date. Zero when
    // the filer didn't report in that quarter (e.g. quarterly skip / closure).
    public int PositionCount { get; set; }
    public long TotalValue { get; set; }
}
