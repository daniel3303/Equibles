using Equibles.Holdings.Data.Models;

namespace Equibles.Web.ViewModels.Holdings;

public class TrendChartsViewModel
{
    public List<AumQuarterlySnapshot> AumSnapshots { get; set; } = [];
    public List<SectorQuarterlySnapshot> SectorAllocations { get; set; } = [];
}
