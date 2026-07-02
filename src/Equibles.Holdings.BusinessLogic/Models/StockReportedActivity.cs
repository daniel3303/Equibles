namespace Equibles.Holdings.BusinessLogic.Models;

/// <summary>
/// What the funds that have already filed the in-progress quarter did in one stock, plus the
/// combined-view totals. Every figure is exact over reported filings only — nothing is
/// extrapolated for funds that have not filed yet (they carry their prior positions).
/// </summary>
public class StockReportedActivity
{
    /// <summary>Distinct holders in the previous (complete) quarter.</summary>
    public int PreviousHolderCount { get; set; }

    /// <summary>
    /// Funds relevant to this stock that have filed the new quarter: its new-quarter filers
    /// plus previous holders who filed elsewhere (including those who dropped the position).
    /// </summary>
    public int ReportedFilerCount { get; set; }

    /// <summary>New-quarter filers of this stock with no position in the previous quarter.</summary>
    public int NewFilerCount { get; set; }

    /// <summary>Previous holders who filed the new quarter without this stock (proven exits).</summary>
    public int SoldOutFilerCount { get; set; }

    /// <summary>
    /// Net share change across the funds that reported: their new-quarter shares (zero for
    /// proven exits) minus their previous-quarter shares (zero for new positions).
    /// </summary>
    public long NetReportedShareDelta { get; set; }

    /// <summary>Distinct holders in the combined view (reported + carried forward).</summary>
    public int CombinedHolderCount { get; set; }

    /// <summary>Total shares in the combined view.</summary>
    public long CombinedShares { get; set; }

    /// <summary>Total reported value (USD) in the combined view.</summary>
    public long CombinedValue { get; set; }
}
