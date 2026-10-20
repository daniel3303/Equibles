namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>
/// Who owns the issuer, Bloomberg OWN style: 13F institutions, insiders and 13D/G-only blocks are
/// summed against shares outstanding, funds are shown but never summed (their shares sit inside
/// their advisers' 13F reports), and what is left is Remaining, flagged rather than clipped when
/// negative.
/// </summary>
public sealed class StockOwnershipSummary
{
    /// <summary>The issuer's entity-wide common shares on today's split basis; null when unknown.</summary>
    public long? SharesOutstanding { get; init; }

    /// <summary>Why percentages of shares outstanding are unavailable; null when they are available.</summary>
    public string PercentUnavailableReason { get; init; }

    public OwnershipSummaryLine Institutional { get; init; }
    public OwnershipSummaryLine Insiders { get; init; }
    public OwnershipSummaryLine Schedule13DGOnly { get; init; }
    public OwnershipSummaryLine Funds { get; init; }

    /// <summary>Shares outstanding minus the summed lines; null when shares outstanding is unknown.</summary>
    public OwnershipSummaryLine Remaining { get; init; }

    public bool RemainingNegative => Remaining is { Shares: < 0 };

    /// <summary>Why insiders are not summed (a multi-class issuer); null when they are.</summary>
    public string InsidersNotSummedReason { get; init; }

    /// <summary>The 13F quarter the institutional line reads, and whether it is the open window's combined view.</summary>
    public DateOnly? InstitutionalAsOf { get; init; }

    public bool InstitutionalCombined { get; init; }

    /// <summary>Summed rows whose split basis is unresolved, so their shares stay as filed.</summary>
    public int SplitUnresolvedRows { get; init; }

    public IReadOnlyList<OwnershipBreakdownLine> ByHolderType { get; init; } = [];
    public IReadOnlyList<OwnershipBreakdownLine> ByStyle { get; init; } = [];
    public IReadOnlyList<OwnershipBreakdownLine> ByLocation { get; init; } = [];

    /// <summary>The newest measured 13F quarter the style split reads, null when none is measured.</summary>
    public DateOnly? StyleAsOf { get; init; }
}
