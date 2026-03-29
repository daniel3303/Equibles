using Equibles.Cboe.Data.Models;

namespace Equibles.Web.ViewModels.Market;

public class PutCallRatioViewModel {
    public CboePutCallRatioType Type { get; set; }
    public string DisplayName { get; set; }
    public List<PutCallRatioItem> Records { get; set; } = [];

    // Statistics
    public decimal? Mean { get; set; }
    public decimal? Median { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public decimal? StdDev { get; set; }
    public decimal? LatestRatio { get; set; }
    public decimal? PreviousRatio { get; set; }
}

public class PutCallRatioItem {
    public DateOnly Date { get; set; }
    public long? CallVolume { get; set; }
    public long? PutVolume { get; set; }
    public long? TotalVolume { get; set; }
    public decimal? PutCallRatio { get; set; }
}
