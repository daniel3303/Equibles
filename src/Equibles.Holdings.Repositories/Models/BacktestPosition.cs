namespace Equibles.Holdings.Repositories.Models;

public class BacktestPosition
{
    public Guid CommonStockId { get; set; }

    /// <summary>
    /// Exact listed ticker for a sibling security; null means the issuer's primary listing.
    /// </summary>
    public string ListedTicker { get; set; }

    public long Shares { get; set; }

    public long Value { get; set; }

    public bool IsOption { get; set; }
}
