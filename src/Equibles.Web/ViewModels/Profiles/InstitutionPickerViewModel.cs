namespace Equibles.Web.ViewModels.Profiles;

// Shared shape for the institution-picker partial used by the overlap / compare /
// combined pages. Each host page builds one of these from its own view model and
// hands it to _InstitutionPicker.cshtml.
public class InstitutionPickerViewModel
{
    public List<InstitutionPick> Picks { get; set; } = [];
    public int MinPicks { get; set; }
    public int MaxPicks { get; set; }
    public List<DateOnly> CommonReportDates { get; set; } = [];
    public DateOnly? SelectedDate { get; set; }
    public string SubmitLabel { get; set; } = "Compare";
}
