using Equibles.Fred.Data.Models;

namespace Equibles.Web.ViewModels.EconomicData;

public class EconomyIndexViewModel {
    public List<EconomyCategoryGroup> Categories { get; set; } = [];
}

public class EconomyCategoryGroup {
    public FredSeriesCategory Category { get; set; }
    public string DisplayName { get; set; }
    public List<EconomySeriesItem> Series { get; set; } = [];
}

public class EconomySeriesItem {
    public string SeriesId { get; set; }
    public string Title { get; set; }
    public string Units { get; set; }
    public string Frequency { get; set; }
    public decimal? LatestValue { get; set; }
    public DateOnly? LatestDate { get; set; }
}
