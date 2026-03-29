using Equibles.Cboe.Data.Models;

namespace Equibles.Web.ViewModels.Market;

public class MarketIndexViewModel {
    public List<PutCallRatioSummary> PutCallRatios { get; set; } = [];
    public VixSummary Vix { get; set; }
}

public class PutCallRatioSummary {
    public CboePutCallRatioType Type { get; set; }
    public string DisplayName { get; set; }
    public decimal? LatestRatio { get; set; }
    public long? LatestCallVolume { get; set; }
    public long? LatestPutVolume { get; set; }
    public DateOnly? LatestDate { get; set; }
}

public class VixSummary {
    public decimal? LatestClose { get; set; }
    public decimal? PreviousClose { get; set; }
    public DateOnly? LatestDate { get; set; }
    public decimal? High52Week { get; set; }
    public decimal? Low52Week { get; set; }
}
