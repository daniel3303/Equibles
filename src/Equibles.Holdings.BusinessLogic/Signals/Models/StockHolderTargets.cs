namespace Equibles.Holdings.BusinessLogic.Signals.Models;

/// <summary>
/// Shareholder targets for one listing at a closed quarter: the peers they were found through
/// (holder-overlap peers, else the sector's largest 13F holdings) and the ranked filers.
/// </summary>
public sealed record StockHolderTargets
{
    public DateOnly? ReportDate { get; init; }

    /// <summary>True when the peers are the sector's largest holdings, for a listing with no overlap peers.</summary>
    public bool SectorFallback { get; init; }

    public string SectorName { get; init; }
    public IReadOnlyList<StockOverlapPeer> Peers { get; init; } = [];
    public IReadOnlyList<StockHolderTarget> Targets { get; init; } = [];

    /// <summary>Filers found before the cap.</summary>
    public int TotalTargets { get; init; }

    public StockHolderTargetSort Sort { get; init; }
    public string Unavailable { get; init; }
}
