using Equibles.Holdings.Repositories.Models;

namespace Equibles.Web.ViewModels.Holdings;

public class TrendChartsViewModel
{
    public List<AumSnapshot> AumSnapshots { get; set; } = [];
    public List<SectorAllocationSnapshot> SectorAllocations { get; set; } = [];
}
