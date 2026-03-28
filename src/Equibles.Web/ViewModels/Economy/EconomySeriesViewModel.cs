using Equibles.Fred.Data.Models;

namespace Equibles.Web.ViewModels.Economy;

public class EconomySeriesViewModel {
    public string SeriesId { get; set; }
    public string Title { get; set; }
    public FredSeriesCategory Category { get; set; }
    public string CategoryDisplayName { get; set; }
    public string Frequency { get; set; }
    public string Units { get; set; }
    public string SeasonalAdjustment { get; set; }
    public List<ObservationItem> Observations { get; set; } = [];

    // Statistics
    public decimal? Mean { get; set; }
    public decimal? Median { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public decimal? StdDev { get; set; }
    public decimal? LatestValue { get; set; }
    public decimal? PreviousValue { get; set; }

    // Moving averages (chronological order, aligned with chart data)
    public List<decimal?> Sma20 { get; set; } = [];
    public List<decimal?> Sma50 { get; set; } = [];
}

public class ObservationItem {
    public DateOnly Date { get; set; }
    public decimal? Value { get; set; }
}
