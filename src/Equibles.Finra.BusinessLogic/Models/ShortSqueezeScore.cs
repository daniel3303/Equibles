namespace Equibles.Finra.BusinessLogic.Models;

/// <summary>
/// One stock's composite short-squeeze score for a FINRA settlement date: the raw
/// factor values and each factor's 0–100 percentile across the scored universe, with
/// <see cref="Score"/> the equal-weight mean of the available factor percentiles.
/// Factors a stock lacks data for are null and simply drop out of its mean.
/// </summary>
public class ShortSqueezeScore
{
    public Guid CommonStockId { get; set; }

    public string Ticker { get; set; }

    public DateOnly SettlementDate { get; set; }

    /// <summary>Market capitalization in USD from the common-stock record; null when unknown.</summary>
    public double? MarketCapitalization { get; set; }

    /// <summary>
    /// Approximate average daily dollar volume in USD: the FINRA-reported average daily
    /// share volume (restated onto today's split basis) times the market-cap-implied
    /// share price. Null when either input is unknown. An estimate for liquidity
    /// gating, not a measured turnover figure.
    /// </summary>
    public double? AverageDailyDollarVolume { get; set; }

    /// <summary>Current short position as a fraction of shares outstanding (0–1 scale).</summary>
    public decimal ShortInterestPercentOfShares { get; set; }

    /// <summary>FINRA days-to-cover; computed from the average daily volume when not reported.</summary>
    public decimal? DaysToCover { get; set; }

    /// <summary>
    /// Change in the short share of total volume: the most recent two weeks' pooled
    /// short-volume ratio minus the prior two weeks'. Positive = rising pressure.
    /// </summary>
    public decimal? ShortVolumeShareTrend { get; set; }

    public double ShortInterestPercentile { get; set; }

    public double? DaysToCoverPercentile { get; set; }

    public double? ShortVolumeTrendPercentile { get; set; }

    /// <summary>Composite 0–100 score: the mean of the available factor percentiles.</summary>
    public double Score { get; set; }
}
