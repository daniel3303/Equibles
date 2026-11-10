namespace Equibles.Yahoo.Repositories;

/// <summary>A presented listing's stored share count and market cap beside its newest traded close.</summary>
public class PresentedCapitalization
{
    public Guid EquityListingId { get; set; }

    public Guid EquityIssuerId { get; set; }

    public long SharesOutstanding { get; set; }

    public double MarketCapitalization { get; set; }

    public decimal? QuoteUnitMultiplier { get; set; }

    public DateTime? SnapshotTime { get; set; }

    public decimal? NewestTradedClose { get; set; }
}
