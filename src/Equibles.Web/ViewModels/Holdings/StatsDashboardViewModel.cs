using Equibles.Holdings.Repositories.Models;

namespace Equibles.Web.ViewModels.Holdings;

public class StatsDashboardViewModel
{
    public List<AumSnapshot> Snapshots { get; set; } = [];
    public AumSnapshot Latest => Snapshots.Count > 0 ? Snapshots[0] : null;
}
