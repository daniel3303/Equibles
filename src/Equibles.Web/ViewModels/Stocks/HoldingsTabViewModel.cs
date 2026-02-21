using Equibles.Holdings.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class HoldingsTabViewModel {
    public List<InstitutionalHolding> Holdings { get; set; } = [];
    public List<DateOnly> AvailableDates { get; set; } = [];
    public DateOnly SelectedDate { get; set; }
    public string Ticker { get; set; }
    public long TotalValue { get; set; }
    public long TotalShares { get; set; }
    public int HolderCount { get; set; }
    public int DisplayedCount { get; set; }
    public Dictionary<Guid, long> PreviousSharesByHolder { get; set; } = [];
}
