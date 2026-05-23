using Equibles.Holdings.Repositories.Models;

namespace Equibles.Web.ViewModels.Profiles;

public class InstitutionOverlapMatrixViewModel
{
    public const int MaxCiks = 10;
    public const int MinCiks = 2;

    public List<string> RequestedCiks { get; set; } = [];
    public List<string> MissingCiks { get; set; } = [];
    public List<DateOnly> CommonReportDates { get; set; } = [];
    public DateOnly? SelectedDate { get; set; }
    public PairwiseOverlapMatrix Matrix { get; set; }
}
