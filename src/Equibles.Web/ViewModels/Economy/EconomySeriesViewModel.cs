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
}

public class ObservationItem {
    public DateOnly Date { get; set; }
    public decimal? Value { get; set; }
}
