using Equibles.Cboe.Data.Models;
using Equibles.Web.ViewModels.Shared;

namespace Equibles.Web.ViewModels.Market;

public class PutCallRatioViewModel : StatsViewModel
{
    public CboePutCallRatioType Type { get; set; }
    public string DisplayName { get; set; }
    public List<PutCallRatioItem> Records { get; set; } = [];

    public decimal? LatestRatio { get; set; }
    public decimal? PreviousRatio { get; set; }
}

public class PutCallRatioItem
{
    public DateOnly Date { get; set; }
    public long? CallVolume { get; set; }
    public long? PutVolume { get; set; }
    public long? TotalVolume { get; set; }
    public decimal? PutCallRatio { get; set; }
}
