namespace Equibles.Holdings.Repositories.Models;

public class ScreenerCriteria
{
    public int? MinFilerCount { get; set; }

    public int? MaxFilerCount { get; set; }

    public int? MinDeltaFilerCount { get; set; }

    public int? MaxDeltaFilerCount { get; set; }

    public long? MinTotalValue { get; set; }

    public long? MaxTotalValue { get; set; }

    public long? MinDeltaValue { get; set; }

    public long? MaxDeltaValue { get; set; }

    // % of float. SharesOutStanding == 0 in the current schema means "unknown" — stocks
    // matching that are excluded from any % filter rather than treated as having 100% float
    // held (which would be a misleading false positive).
    public double? MinPctFloat { get; set; }

    public double? MaxPctFloat { get; set; }

    public int? MinNewPositions { get; set; }

    public int? MinSoldOutPositions { get; set; }

    public List<Guid> IndustryIds { get; set; } = [];
}
