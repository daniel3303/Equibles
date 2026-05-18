namespace Equibles.Web.ViewModels.Profiles;

public class CongressProfileViewModel
{
    public string Name { get; set; }
    public List<CongressTradeRowViewModel> Trades { get; set; } = [];
}
