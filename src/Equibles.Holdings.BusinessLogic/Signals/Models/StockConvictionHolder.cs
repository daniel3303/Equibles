using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Signals.Models;

/// <summary>A 13F filer holding a listing, with the listing's weight in its valued book.</summary>
public sealed record StockConvictionHolder
{
    public Guid HolderId { get; init; }
    public string Cik { get; init; }
    public string Name { get; init; }
    public long Shares { get; init; }
    public long Value { get; init; }

    /// <summary>Value over the filer's valued 13F book that quarter, from 0 to 1.</summary>
    public decimal Weight { get; init; }

    public long EquityAum { get; init; }
    public HolderStyleBand? StyleBand { get; init; }
    public HolderEntityType? EntityType { get; init; }
}
