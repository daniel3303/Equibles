using Equibles.Web.ViewModels.Shared;

namespace Equibles.Web.ViewModels.Market;

public class VixViewModel : StatsViewModel
{
    public List<VixDailyItem> Records { get; set; } = [];

    public decimal? LatestClose { get; set; }
    public decimal? PreviousClose { get; set; }
    public List<decimal?> Sma20 { get; set; } = [];
    public List<decimal?> Sma50 { get; set; } = [];
}

public class VixDailyItem
{
    public DateOnly Date { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
}
