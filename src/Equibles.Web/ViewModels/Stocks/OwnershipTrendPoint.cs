namespace Equibles.Web.ViewModels.Stocks;

// One point of the per-quarter institutional ownership trend: total shares
// held isolates accumulation/distribution from price moves, which a USD
// total would conflate.
public class OwnershipTrendPoint
{
    public DateOnly ReportDate { get; set; }
    public long TotalShares { get; set; }
    public int HolderCount { get; set; }
}
