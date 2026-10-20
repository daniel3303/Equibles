namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>
/// The summed holders that roll up to one ultimate parent: its 13F filers, which the 13F rules keep
/// disjoint, plus the insider and 13D/G-only blocks no 13F report contains.
/// </summary>
public sealed class UltimateHolderGroup
{
    public string UltimateCik { get; init; }
    public string Name { get; init; }
    public long Shares { get; init; }
    public long Form13FShares { get; init; }
    public decimal? PercentOutstanding { get; set; }

    /// <summary>The summed rows grouped here, largest first.</summary>
    public IReadOnlyList<StockHolderRow> Members { get; init; } = [];
}
