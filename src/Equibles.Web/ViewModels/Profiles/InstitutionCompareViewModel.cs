using Equibles.Holdings.Repositories.Models;

namespace Equibles.Web.ViewModels.Profiles;

public class InstitutionCompareViewModel
{
    public List<string> RequestedCiks { get; set; } = [];
    public List<string> MissingCiks { get; set; } = [];
    public List<DateOnly> CommonReportDates { get; set; } = [];
    public DateOnly? SelectedDate { get; set; }
    public FundOverlapResult Overlap { get; set; }

    public const int MaxCiks = 4;
    public const int MinCiks = 2;
}
