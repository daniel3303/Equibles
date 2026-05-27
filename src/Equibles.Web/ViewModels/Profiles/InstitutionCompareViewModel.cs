using Equibles.Holdings.Repositories.Models;

namespace Equibles.Web.ViewModels.Profiles;

public class InstitutionCompareViewModel : MultiInstitutionViewModel
{
    public FundOverlapResult Overlap { get; set; }

    public const int MaxCiks = 4;
    public const int MinCiks = 2;
}
