namespace Equibles.Web.ViewModels.Holdings;

public class ScreenerCriteriaViewModel
{
    public int? MinFilerCount { get; set; }

    public int? MaxFilerCount { get; set; }

    public int? MinDeltaFilerCount { get; set; }

    public int? MaxDeltaFilerCount { get; set; }

    public long? MinTotalValue { get; set; }

    public long? MaxTotalValue { get; set; }

    public long? MinDeltaValue { get; set; }

    public long? MaxDeltaValue { get; set; }

    public double? MinPctFloat { get; set; }

    public double? MaxPctFloat { get; set; }

    public int? MinNewPositions { get; set; }

    public int? MinSoldOutPositions { get; set; }

    public List<Guid> IndustryIds { get; set; } = [];
}
