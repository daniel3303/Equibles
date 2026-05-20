using Equibles.Holdings.Repositories.Models;

namespace Equibles.Web.ViewModels.Profiles;

public class InstitutionProfileViewModel
{
    public string Name { get; set; }
    public string Cik { get; set; }
    public string Location { get; set; }
    public List<HoldingRowViewModel> Holdings { get; set; } = [];
    public InstitutionPortfolioSummary Summary { get; set; } = new();
}
