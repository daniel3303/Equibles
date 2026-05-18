namespace Equibles.Web.ViewModels.Profiles;

public class InsiderProfileViewModel
{
    public string Name { get; set; }
    public string OwnerCik { get; set; }
    public string Location { get; set; }
    public string Role { get; set; }
    public List<InsiderTradeRowViewModel> Transactions { get; set; } = [];
}
