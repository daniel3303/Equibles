using Equibles.Holdings.Repositories.Models;

namespace Equibles.Web.ViewModels.Profiles;

public class InstitutionOverlapMatrixViewModel : MultiInstitutionViewModel
{
    public const int MaxCiks = 10;
    public const int MinCiks = 2;

    public PairwiseOverlapMatrix Matrix { get; set; }
}
