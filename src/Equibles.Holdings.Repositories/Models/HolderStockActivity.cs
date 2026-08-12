namespace Equibles.Holdings.Repositories.Models;

public sealed class HolderStockActivity
{
    public Guid InstitutionalHolderId { get; set; }

    // Null identifies the issuer's primary listing. Sibling-listing rows stay separate until
    // read-time split restatement; combining them here would apply the primary split factor to
    // a class whose own price series has a different split history.
    public string ListedTicker { get; set; }

    public long CurrentShares { get; set; }

    public long PreviousShares { get; set; }

    public long CurrentValue { get; set; }

    public long PreviousValue { get; set; }

    // Row presence cannot be inferred from the aggregate share count: a filed zero-share row
    // still proves that the holder reported in the current quarter.
    public int CurrentPositionCount { get; set; }

    public int PreviousPositionCount { get; set; }
}
