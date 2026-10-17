namespace Equibles.Holdings.HostedService.Services;

/// <summary>The median value per share 13F filers reported for one security at a quarter end.</summary>
public class QuarterFiledPriceRow
{
    public Guid EquityIssuerId { get; set; }
    public string ListedTicker { get; set; }
    public double FiledPricePerShare { get; set; }
}
