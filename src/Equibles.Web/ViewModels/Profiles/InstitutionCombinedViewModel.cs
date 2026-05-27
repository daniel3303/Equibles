using Equibles.Holdings.Repositories.Models;

namespace Equibles.Web.ViewModels.Profiles;

public class InstitutionCombinedViewModel : MultiInstitutionViewModel
{
    public FundOverlapResult Overlap { get; set; }

    // Pre-computed per-row consensus count (number of funds with Value > 0 in the row's
    // slices). Ranking sort key — populated alongside Overlap by the controller.
    public Dictionary<Guid, int> FundsHoldingByStock { get; set; } = [];

    public const int MaxCiks = 25;
    public const int MinCiks = 2;
}
