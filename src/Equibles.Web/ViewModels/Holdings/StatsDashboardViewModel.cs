using Equibles.Holdings.Data.Models;

namespace Equibles.Web.ViewModels.Holdings;

public class StatsDashboardViewModel
{
    public List<AumQuarterlySnapshot> Snapshots { get; set; } = [];
    public AumQuarterlySnapshot Latest => Snapshots.Count > 0 ? Snapshots[0] : null;
}
