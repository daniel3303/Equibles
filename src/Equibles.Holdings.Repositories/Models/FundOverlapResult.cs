namespace Equibles.Holdings.Repositories.Models;

public class FundOverlapResult
{
    public DateOnly ReportDate { get; set; }
    public List<FundOverlapFund> Funds { get; set; } = [];
    public List<FundOverlapRow> Rows { get; set; } = [];

    // Counts and intersection-based stats across the union of the funds' positions.
    public int UnionPositionCount { get; set; }
    public int IntersectionPositionCount { get; set; }
    public double JaccardSimilarityPercent { get; set; }

    // Dollar-weighted overlap: SUM over shared stocks of min(per-fund value across the
    // funds), divided by SUM over the union of max value. Approximates "if you held the
    // overlap, how much of the average-sized fund would that be?"
    public double DollarWeightedOverlapPercent { get; set; }
}

public class FundOverlapFund
{
    public Guid HolderId { get; set; }
    public string HolderCik { get; set; }
    public string HolderName { get; set; }
    public int PositionCount { get; set; }
    public long TotalValue { get; set; }
}

public class FundOverlapRow
{
    public Guid CommonStockId { get; set; }
    public string Ticker { get; set; }
    public string Name { get; set; }

    // Per-fund slice indexed by the same order as FundOverlapResult.Funds.
    public List<FundOverlapRowSlice> Slices { get; set; } = [];

    // True when every listed fund reports this stock.
    public bool IsCommon { get; set; }
    public long CombinedValue { get; set; }
}

public class FundOverlapRowSlice
{
    public Guid HolderId { get; set; }
    public long Shares { get; set; }
    public long Value { get; set; }
    public double PercentOfPortfolio { get; set; }
}
