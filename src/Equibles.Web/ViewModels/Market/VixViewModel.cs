namespace Equibles.Web.ViewModels.Market;

public class VixViewModel {
    public List<VixDailyItem> Records { get; set; } = [];

    // Statistics
    public decimal? Mean { get; set; }
    public decimal? Median { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public decimal? StdDev { get; set; }
    public decimal? LatestClose { get; set; }
    public decimal? PreviousClose { get; set; }
    public List<decimal?> Sma20 { get; set; } = [];
    public List<decimal?> Sma50 { get; set; } = [];
}

public class VixDailyItem {
    public DateOnly Date { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
}
