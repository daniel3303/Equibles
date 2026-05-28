using Equibles.Fred.Data.Models;
using Equibles.Web.ViewModels.Shared;

namespace Equibles.Web.ViewModels.EconomicData;

public class EconomySeriesViewModel : StatsViewModel
{
    public string SeriesId { get; set; }
    public string Title { get; set; }
    public FredSeriesCategory Category { get; set; }
    public string CategoryDisplayName { get; set; }
    public string Frequency { get; set; }
    public string Units { get; set; }
    public string SeasonalAdjustment { get; set; }
    public List<ObservationItem> Observations { get; set; } = [];

    public decimal? LatestValue { get; set; }
    public decimal? PreviousValue { get; set; }

    // Moving averages (chronological order, aligned with chart data)
    public List<decimal?> Sma20 { get; set; } = [];
    public List<decimal?> Sma50 { get; set; } = [];
}

public class ObservationItem
{
    public DateOnly Date { get; set; }
    public decimal? Value { get; set; }
}
