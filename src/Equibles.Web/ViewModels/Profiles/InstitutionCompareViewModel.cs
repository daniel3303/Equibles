using Equibles.Holdings.Repositories.Models;

namespace Equibles.Web.ViewModels.Profiles;

public class InstitutionCompareViewModel
{
    public List<string> RequestedCiks { get; set; } = [];
    public List<string> MissingCiks { get; set; } = [];
    public List<DateOnly> CommonReportDates { get; set; } = [];
    public DateOnly? SelectedDate { get; set; }
    public FundOverlapResult Overlap { get; set; }

    // Resolved name + CIK pairs for chip rendering on first paint; see
    // [InstitutionOverlapMatrixViewModel.InitialPicks] for the same contract.
    public List<InstitutionPick> InitialPicks { get; set; } = [];

    public const int MaxCiks = 4;
    public const int MinCiks = 2;
}
