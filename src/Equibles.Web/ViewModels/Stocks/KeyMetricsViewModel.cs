namespace Equibles.Web.ViewModels.Stocks;

public class KeyMetricsViewModel
{
    public decimal? LatestClose { get; set; }
    public decimal? High52Week { get; set; }
    public decimal? Low52Week { get; set; }
    public double MarketCapitalization { get; set; }
    public decimal? EpsDiluted { get; set; }
    public decimal? PeRatio { get; set; }
}
