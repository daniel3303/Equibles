using System.Globalization;
using Equibles.Holdings.BusinessLogic.Intelligence.Benchmarks;
using Equibles.Holdings.Data.Models;
using static Equibles.Holdings.BusinessLogic.Intelligence.HolderStyleBandClassifier;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// Plain-language reasons for a measured quarter's style, turnover and horizon bands. Each names
/// the measurement and the threshold it met, so a band always reads as a measurement of the filed
/// book, never as what the firm calls itself.
/// </summary>
public static class HolderStyleDescriptions
{
    /// <summary>The benchmark behind <see cref="HolderQuarterlyMetrics.AggregateActiveShare"/>.</summary>
    public const string AggregateBenchmark = "every 13F filer's combined book";

    /// <summary>
    /// The benchmark a stored row names, with its as-of date when it is a published index: "the
    /// S&amp;P 500 as of 2026-06-30", or <see cref="AggregateBenchmark"/>.
    /// </summary>
    public static string Benchmark(string key, string name, DateOnly? asOf)
    {
        if (key == HolderBenchmarkCatalog.Aggregate)
            return AggregateBenchmark;
        var label = name ?? HolderBenchmarkCatalog.Name(key) ?? key;
        return asOf is { } date
            ? $"the {label} as of {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            : $"the {label}";
    }

    /// <summary>
    /// The closest benchmark and the book's active share against it; a row measured before the
    /// published indexes were benchmarks reads the combined 13F book.
    /// </summary>
    public static (decimal? ActiveShare, string Benchmark) Closest(
        HolderQuarterlyMetrics metrics
    ) =>
        metrics.ClosestBenchmark == null
            ? (metrics.AggregateActiveShare, AggregateBenchmark)
            : (
                metrics.ClosestActiveShare,
                Benchmark(
                    metrics.ClosestBenchmark,
                    metrics.ClosestBenchmarkName,
                    metrics.ClosestBenchmarkAsOf
                )
            );

    /// <summary>
    /// One line naming the closest benchmark and the sector active share, for a row under the
    /// bands; null when the book was measured against no benchmark.
    /// </summary>
    public static string BenchmarkSummary(HolderQuarterlyMetrics metrics)
    {
        var (activeShare, benchmark) = Closest(metrics);
        if (activeShare == null)
            return null;
        var sector = metrics.SectorActiveShare is { } sectorShare
            ? Invariant(
                $"; sector active share {Percent(sectorShare)} against the combined 13F book's sector weights"
            )
            : "";
        return Invariant(
            $"Closest benchmark: {benchmark}, active share {Percent(activeShare)}{sector}"
        );
    }

    public static string Style(HolderQuarterlyMetrics metrics) =>
        metrics.StyleBand switch
        {
            HolderStyleBand.IndexLike
                when metrics.IndexLikeEvidence == HolderIndexLikeEvidence.IndexFundAssets =>
                IndexFundAssets(metrics),
            HolderStyleBand.IndexLike => Invariant(
                $"Index-like: active share {Percent(Closest(metrics).ActiveShare)} against {Closest(metrics).Benchmark}, across {metrics.StockCount:N0} stocks (index-like at {Threshold(IndexLikeMaximumActiveShare)} or less with {IndexLikeMinimumStockCount}+ stocks)"
            ),
            HolderStyleBand.SystematicLike => Invariant(
                $"Systematic-like: {metrics.StockCount:N0} stocks ({SystematicMinimumStockCount}+), trailing annual turnover {Percent(metrics.TrailingAnnualTurnover)} ({Threshold(SystematicMinimumAnnualTurnover)}+), top 10 weight {Percent(metrics.TopTenWeight)} ({Threshold(SystematicMaximumTopTenWeight)} or less)"
            ),
            HolderStyleBand.Concentrated
                when metrics.TopTenWeight >= ConcentratedMinimumTopTenWeight => Invariant(
                $"Concentrated: the top 10 positions are {Percent(metrics.TopTenWeight)} of the book ({Threshold(ConcentratedMinimumTopTenWeight)}+)"
            ),
            HolderStyleBand.Concentrated => Invariant(
                $"Concentrated: {Number(metrics.EffectivePositions)} effective positions ({ConcentratedMaximumEffectivePositions:0} or fewer)"
            ),
            HolderStyleBand.DiversifiedActive => Invariant(
                $"Diversified active: top 10 weight {Percent(metrics.TopTenWeight)}, {Number(metrics.EffectivePositions)} effective positions{(Closest(metrics) is ({ } activeShare, var against) ? $", active share {Percent(activeShare)} against {against}" : "")}; it meets none of the index-like, systematic-like or concentrated tests"
            ),
            _ => Unbanded(metrics),
        };

    public static string Turnover(HolderQuarterlyMetrics metrics) =>
        metrics.TurnoverBand switch
        {
            HolderTurnoverBand.Low => Invariant(
                $"Low turnover: {Percent(metrics.TrailingAnnualTurnover)} over the last four quarters (under {Threshold(LowTurnoverBelow)})"
            ),
            HolderTurnoverBand.Medium => Invariant(
                $"Medium turnover: {Percent(metrics.TrailingAnnualTurnover)} over the last four quarters ({Threshold(LowTurnoverBelow)} to under {Threshold(HighTurnoverFrom)})"
            ),
            HolderTurnoverBand.High => Invariant(
                $"High turnover: {Percent(metrics.TrailingAnnualTurnover)} over the last four quarters ({Threshold(HighTurnoverFrom)}+)"
            ),
            _ => "Turnover unknown: it needs four straight quarters of measured turnover",
        };

    public static string Horizon(HolderQuarterlyMetrics metrics) =>
        metrics.HorizonBand switch
        {
            HolderHorizonBand.Short => Invariant(
                $"Short horizon: value-weighted holding period {Number(metrics.ValueWeightedTenureQuarters)} quarters (under {ShortHorizonBelowQuarters:0})"
            ),
            HolderHorizonBand.Medium => Invariant(
                $"Medium horizon: value-weighted holding period {Number(metrics.ValueWeightedTenureQuarters)} quarters ({ShortHorizonBelowQuarters:0} to under {LongHorizonFromQuarters:0})"
            ),
            HolderHorizonBand.Long => Invariant(
                $"Long horizon: value-weighted holding period {Number(metrics.ValueWeightedTenureQuarters)} quarters ({LongHorizonFromQuarters:0}+){(metrics.CensoredValueShare > 0 ? $", a floor since {Percent(metrics.CensoredValueShare)} of the book was already held when coverage starts" : "")}"
            ),
            _ when metrics.ValueWeightedTenureQuarters is { } quarters => Invariant(
                $"Horizon unknown: the value-weighted holding period is at least {Number(quarters)} quarters, but {Percent(metrics.CensoredValueShare)} of the book was already held when coverage starts"
            ),
            _ => "Horizon unknown: no valued positions",
        };

    /// <summary>Every band's rule with its thresholds, for a footnote under the bands.</summary>
    public static string Methodology =>
        Invariant(
            $"Style bands measure the filed 13F common-share book each closed quarter, tested in this order. Unknown: under ${MinimumEquityAum:N0} of valued equity or {MinimumStockCount} stocks. Index-like ({IndexLikeMinimumStockCount}+ stocks): active share of {Threshold(IndexLikeMaximumActiveShare)} or less against the closest benchmark, which is {IndexBenchmarks} (weighted by an index-tracking fund's reported holdings or the index's constituents, nearest the quarter end) or {AggregateBenchmark}, or index series holding {Threshold(IndexLikeMinimumIndexFundShare)}+ of the registered fund assets it advises (N-CEN) and equal to {Threshold(IndexLikeMinimumIndexAssetsToBook)}+ of its book. Systematic-like: {SystematicMinimumStockCount}+ stocks, {Threshold(SystematicMinimumAnnualTurnover)}+ annual turnover and a top 10 weight of {Threshold(SystematicMaximumTopTenWeight)} or less. Concentrated: a top 10 weight of {Threshold(ConcentratedMinimumTopTenWeight)}+ or {ConcentratedMaximumEffectivePositions:0} or fewer effective positions. Diversified active: none of these. Turnover is the smaller of buys and sells over average equity value, summed over four quarters: low under {Threshold(LowTurnoverBelow)}, high {Threshold(HighTurnoverFrom)}+. Horizon is the value-weighted number of quarters each position has been held: short under {ShortHorizonBelowQuarters:0}, long {LongHorizonFromQuarters:0}+."
        );

    // Every published index in the catalog, listed for the methodology.
    private static string IndexBenchmarks
    {
        get
        {
            var names = HolderBenchmarkCatalog
                .All.Where(entry => entry.Key != HolderBenchmarkCatalog.Aggregate)
                .Select(entry => entry.Name)
                .ToList();
            return $"the {string.Join(", ", names.SkipLast(1))} or {names[^1]}";
        }
    }

    private static string IndexFundAssets(HolderQuarterlyMetrics metrics)
    {
        var index = metrics.IndexSeriesNetAssets.GetValueOrDefault();
        var advised = metrics.AdvisedSeriesNetAssets.GetValueOrDefault();
        var fundShare = advised > 0 ? index / advised : (decimal?)null;
        var bookShare = metrics.EquityAum > 0 ? index / metrics.EquityAum : (decimal?)null;
        var asOf = metrics.AdvisedSeriesAsOf is { } date
            ? $", N-CEN as of {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            : "";
        return Invariant(
            $"Index-like: index series hold {Percent(fundShare)} of the ${advised:N0} in registered funds it advises ({Threshold(IndexLikeMinimumIndexFundShare)}+), equal to {Percent(bookShare)} of its 13F equity book ({Threshold(IndexLikeMinimumIndexAssetsToBook)}+), across {metrics.StockCount:N0} stocks ({IndexLikeMinimumStockCount}+){asOf}"
        );
    }

    private static string Unbanded(HolderQuarterlyMetrics metrics)
    {
        if (metrics.EquityAum < MinimumEquityAum)
            return Invariant(
                $"Unknown: ${metrics.EquityAum:N0} of valued equity, under the ${MinimumEquityAum:N0} needed to band a style"
            );
        if (metrics.StockCount < MinimumStockCount)
            return Invariant(
                $"Unknown: {metrics.StockCount} stocks, under the {MinimumStockCount} needed to band a style"
            );
        return "Unknown: no valued positions to weigh";
    }

    private static string Percent(decimal? fraction) =>
        fraction is { } value ? Invariant($"{value * 100:0.0}%") : "unknown";

    private static string Threshold(decimal fraction) => Invariant($"{fraction * 100:0.##}%");

    private static string Number(decimal? value) =>
        value is { } number ? Invariant($"{number:0.0}") : "unknown";

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
