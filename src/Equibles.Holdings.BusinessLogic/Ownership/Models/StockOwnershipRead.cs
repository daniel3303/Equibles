using Equibles.Holdings.BusinessLogic.Models;

namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>
/// A listing's unified holder list and ownership summary. A cached read keeps only the summary
/// and the first default-view rows (<see cref="Complete"/> is false); a complete read also holds
/// every lane row and the ultimate-parent groups.
/// </summary>
public sealed class StockOwnershipRead
{
    public string ListedTicker { get; init; }
    public bool ExactListing { get; init; }
    public StockQuarterAnchor Anchor { get; init; }
    public StockOwnershipSummary Summary { get; init; }

    /// <summary>The default view: one top-level row per holder, largest first, others nested.</summary>
    public IReadOnlyList<StockHolderRow> DefaultRows { get; init; } = [];

    /// <summary>How many top-level rows the default view has, including any a cached read dropped.</summary>
    public int DefaultRowCount { get; init; }

    /// <summary>Every row from every lane, stale and superseded ones included; empty on a cached read.</summary>
    public IReadOnlyList<StockHolderRow> Rows { get; init; } = [];

    public IReadOnlyList<UltimateHolderGroup> UltimateGroups { get; init; } = [];

    public bool Complete { get; init; }
}
