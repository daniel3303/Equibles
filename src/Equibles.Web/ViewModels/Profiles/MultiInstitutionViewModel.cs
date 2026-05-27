namespace Equibles.Web.ViewModels.Profiles;

public abstract class MultiInstitutionViewModel
{
    public List<string> RequestedCiks { get; set; } = [];
    public List<string> MissingCiks { get; set; } = [];
    public List<DateOnly> CommonReportDates { get; set; } = [];
    public DateOnly? SelectedDate { get; set; }

    // Resolved name + CIK pairs for the chips the picker renders on first load — one
    // entry per requested CIK, with Name == null when the CIK is missing from the
    // database. Driven server-side so the chips look complete even before JS boots.
    public List<InstitutionPick> InitialPicks { get; set; } = [];
}
