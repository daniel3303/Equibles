namespace Equibles.Holdings.Repositories.Models;

public class MarketWideStockChurn
{
    public Guid CommonStockId { get; set; }

    // Filers who reported a position in the current quarter but not in the prior quarter.
    public int NewFilerCount { get; set; }

    // Filers who reported a position in the prior quarter but not in the current quarter.
    public int SoldOutFilerCount { get; set; }
}
