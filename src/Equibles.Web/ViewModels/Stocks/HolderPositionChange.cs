using Equibles.Holdings.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class HolderPositionChange
{
    public Guid InstitutionalHolderId { get; set; }
    public InstitutionalHolder InstitutionalHolder { get; set; }

    // null when SoldOut — the holder reported nothing this quarter.
    public InstitutionalHolding CurrentHolding { get; set; }

    public long CurrentShares { get; set; }
    public long PreviousShares { get; set; }
    public long CurrentValue { get; set; }
    public long PreviousValue { get; set; }

    public PositionChangeType ChangeType { get; set; }

    public long DeltaShares => CurrentShares - PreviousShares;
    public long DeltaValue => CurrentValue - PreviousValue;
}
