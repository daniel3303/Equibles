namespace Equibles.Web.ViewModels.Stocks;

public class HoldingsTabViewModel
{
    public List<DateOnly> AvailableDates { get; set; } = [];
    public DateOnly SelectedDate { get; set; }
    public string Ticker { get; set; }
    public long TotalValue { get; set; }
    public long TotalShares { get; set; }
    public int HolderCount { get; set; }
    public long SharesOutstanding { get; set; }

    public Dictionary<PositionChangeType, List<HolderPositionChange>> GroupedHolders { get; set; } =
    [];
    public Dictionary<PositionChangeType, int> BucketCounts { get; set; } = [];

    // Top movers for the selected quarter — pre-sorted, capped to TopMoversPreviewCount.
    // Buyers = New ∪ Increased (Δ shares > 0), Sellers = Reduced ∪ Sold out (Δ shares < 0).
    public List<HolderPositionChange> TopBuyers { get; set; } = [];
    public List<HolderPositionChange> TopSellers { get; set; } = [];
    public int TotalBuyerCount { get; set; }
    public int TotalSellerCount { get; set; }

    public HashSet<PositionChangeType> ActiveTypes { get; set; }

    public bool IsCombinedView { get; set; }

    public bool IsTypeActive(PositionChangeType type) =>
        ActiveTypes == null || ActiveTypes.Contains(type);

    public const int TopMoversPreviewCount = 5;
}
