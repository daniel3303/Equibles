using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Repositories.Models;

public class MarketWideStockActivity
{
    public Guid CommonStockId { get; set; }
    public long CurrentShares { get; set; }
    public long PreviousShares { get; set; }
    public long CurrentValue { get; set; }
    public long PreviousValue { get; set; }
    public int CurrentFilerCount { get; set; }
    public int PreviousFilerCount { get; set; }

    // The part of each total held by filers without a 13F for the other quarter (a first 13F, a
    // return after a gap, a filer not yet filed or filing under confidential treatment). A
    // missing filing is no trade, so the deltas and the buy and sell boards leave it out.
    public long UnmatchedCurrentShares { get; set; }
    public long UnmatchedPreviousShares { get; set; }
    public long UnmatchedCurrentValue { get; set; }
    public long UnmatchedPreviousValue { get; set; }

    public IReadOnlyList<StockQuarterlyListingActivity> ListingShares { get; set; } = [];

    // Changes over the filers with a 13F in both quarters.
    public long DeltaShares =>
        (CurrentShares - UnmatchedCurrentShares) - (PreviousShares - UnmatchedPreviousShares);
    public long DeltaValue =>
        (CurrentValue - UnmatchedCurrentValue) - (PreviousValue - UnmatchedPreviousValue);
}
