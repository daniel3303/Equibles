namespace Equibles.Finra.BusinessLogic.Models;

/// <summary>
/// One stock's composite short-squeeze score for a FINRA settlement date: the raw
/// factor values, each factor's 0–100 percentile across the scored universe, the
/// catalyst flags, and the resulting <see cref="Score"/>. The composite is the
/// weight-averaged mean of the available factor percentiles (see
/// <see cref="ShortSqueezeScoreManager"/> for the weights) plus the catalyst boost,
/// clamped to 100. Factors a stock lacks data for are null and drop out of the
/// weighted mean, their weight redistributed over the factors that remain.
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

    /// <summary>
    /// Change in the short position versus the previous FINRA report, as a fraction
    /// of the previous position (0.15 = shorts grew 15%), with both positions
    /// restated onto today's split basis. Positive = the short crowd is building.
    /// Null when the previous position is unknown or zero.
    /// </summary>
    public decimal? ShortInterestChangePercent { get; set; }

    /// <summary>
    /// The worst single-day fails-to-deliver quantity over the trailing window
    /// (restated onto today's split basis) as a fraction of shares outstanding.
    /// Persistent or spiking fails signal delivery pressure in the lending market —
    /// the closest public analog to hard-to-borrow conditions. Zero means the SEC
    /// feed reported no fails for the stock; null means the feed itself had no data.
    /// </summary>
    public decimal? FailsToDeliverPercentOfShares { get; set; }

    /// <summary>
    /// The latest close versus the trailing volume-weighted average price, as a
    /// fraction of that average (0.25 = price sits 25% above where recent volume
    /// traded). A proxy for how far open shorts are underwater — the capital-
    /// constraint condition squeezes ignite from. Null without enough price history.
    /// </summary>
    public decimal? PriceAboveVwap { get; set; }

    /// <summary>
    /// True when the last trading week's return is positive and statistically
    /// extreme versus the stock's own daily volatility — a squeeze may already be
    /// igniting.
    /// </summary>
    public bool HasPriceSpikeCatalyst { get; set; }

    /// <summary>
    /// True when the last trading week's dollar turnover runs a multiple of the
    /// stock's baseline while the price moves against the shorts.
    /// </summary>
    public bool HasVolumeSurgeCatalyst { get; set; }

    public double ShortInterestPercentile { get; set; }

    public double? DaysToCoverPercentile { get; set; }

    public double? ShortVolumeTrendPercentile { get; set; }

    public double? ShortInterestChangePercentile { get; set; }

    public double? FailsToDeliverPercentile { get; set; }

    public double? PriceAboveVwapPercentile { get; set; }

    /// <summary>
    /// The weighted mean of the available factor percentiles, before the catalyst
    /// boost — the structural "how crowded and stressed is the short side" reading.
    /// </summary>
    public double BaseScore { get; set; }

    /// <summary>
    /// Rank points added for active catalysts (see the manager's boost constants),
    /// capped at <see cref="ShortSqueezeScoreManager.MaxCatalystBoost"/>.
    /// </summary>
    public double CatalystBoost { get; set; }

    /// <summary>
    /// Composite 0–100 score: <see cref="BaseScore"/> plus <see cref="CatalystBoost"/>,
    /// clamped to 100.
    /// </summary>
    public double Score { get; set; }
}
