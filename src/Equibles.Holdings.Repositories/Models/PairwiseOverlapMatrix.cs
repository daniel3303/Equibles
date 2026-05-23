namespace Equibles.Holdings.Repositories.Models;

public class PairwiseOverlapMatrix
{
    public DateOnly ReportDate { get; set; }
    public List<FundOverlapFund> Funds { get; set; } = [];
    public int[][] SharedTickerCounts { get; set; } = [];
}
