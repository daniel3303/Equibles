using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// Reads a filer's style bands from its measurements in one quarter. Every band is a measurement
/// with stated thresholds, never a claim about the firm; bump <see cref="BandVersion"/> when a
/// threshold or rule changes so stored bands are recomputed.
/// </summary>
public static class HolderStyleBandClassifier
{
    public const int BandVersion = 1;

    public const long MinimumEquityAum = 10_000_000;
    public const int MinimumStockCount = 5;

    public const decimal IndexLikeMaximumActiveShare = 0.20m;
    public const int IndexLikeMinimumStockCount = 100;

    /// <summary>Index series' share of the registered fund assets the filer advises.</summary>
    public const decimal IndexLikeMinimumIndexFundShare = 0.80m;

    /// <summary>
    /// Index series' net assets as a share of the filer's 13F equity book, so a small index fund
    /// family never labels a large discretionary book.
    /// </summary>
    public const decimal IndexLikeMinimumIndexAssetsToBook = 0.50m;

    public const int SystematicMinimumStockCount = 500;
    public const decimal SystematicMinimumAnnualTurnover = 1.00m;
    public const decimal SystematicMaximumTopTenWeight = 0.25m;

    public const decimal ConcentratedMinimumTopTenWeight = 0.60m;
    public const decimal ConcentratedMaximumEffectivePositions = 12m;

    public const decimal LowTurnoverBelow = 0.30m;
    public const decimal HighTurnoverFrom = 1.00m;

    public const decimal ShortHorizonBelowQuarters = 4m;
    public const decimal LongHorizonFromQuarters = 12m;

    public static HolderStyleClassification Classify(HolderStyleInputs inputs)
    {
        var (style, evidence) = Style(inputs);
        return new HolderStyleClassification(
            style,
            evidence,
            TurnoverBand(inputs.TrailingAnnualTurnover),
            HorizonBand(inputs.ValueWeightedTenureQuarters, inputs.CensoredValueShare)
        );
    }

    private static (HolderStyleBand, HolderIndexLikeEvidence) Style(HolderStyleInputs inputs)
    {
        if (
            inputs.EquityAum < MinimumEquityAum
            || inputs.StockCount < MinimumStockCount
            || inputs.TopTenWeight == null
        )
            return (HolderStyleBand.Unknown, HolderIndexLikeEvidence.None);

        if (inputs.StockCount >= IndexLikeMinimumStockCount)
        {
            if (inputs.ClosestActiveShare <= IndexLikeMaximumActiveShare)
                return (HolderStyleBand.IndexLike, HolderIndexLikeEvidence.ActiveShare);
            if (MostlyIndexFunds(inputs))
                return (HolderStyleBand.IndexLike, HolderIndexLikeEvidence.IndexFundAssets);
        }

        if (
            inputs.StockCount >= SystematicMinimumStockCount
            && inputs.TrailingAnnualTurnover >= SystematicMinimumAnnualTurnover
            && inputs.TopTenWeight <= SystematicMaximumTopTenWeight
        )
            return (HolderStyleBand.SystematicLike, HolderIndexLikeEvidence.None);

        if (
            inputs.TopTenWeight >= ConcentratedMinimumTopTenWeight
            || inputs.EffectivePositions <= ConcentratedMaximumEffectivePositions
        )
            return (HolderStyleBand.Concentrated, HolderIndexLikeEvidence.None);

        return (HolderStyleBand.DiversifiedActive, HolderIndexLikeEvidence.None);
    }

    private static bool MostlyIndexFunds(HolderStyleInputs inputs) =>
        inputs is { IndexSeriesNetAssets: > 0, AdvisedSeriesNetAssets: > 0 }
        && inputs.IndexSeriesNetAssets.Value
            >= IndexLikeMinimumIndexFundShare * inputs.AdvisedSeriesNetAssets.Value
        && inputs.IndexSeriesNetAssets.Value
            >= IndexLikeMinimumIndexAssetsToBook * inputs.EquityAum;

    public static HolderTurnoverBand TurnoverBand(decimal? trailingAnnualTurnover) =>
        trailingAnnualTurnover switch
        {
            null => HolderTurnoverBand.Unknown,
            < LowTurnoverBelow => HolderTurnoverBand.Low,
            < HighTurnoverFrom => HolderTurnoverBand.Medium,
            _ => HolderTurnoverBand.High,
        };

    /// <summary>
    /// A censored tenure is only a lower bound, so it can still reach Long but cannot be called
    /// Short or Medium.
    /// </summary>
    public static HolderHorizonBand HorizonBand(
        decimal? valueWeightedTenureQuarters,
        decimal? censoredValueShare
    ) =>
        valueWeightedTenureQuarters switch
        {
            null => HolderHorizonBand.Unknown,
            >= LongHorizonFromQuarters => HolderHorizonBand.Long,
            _ when censoredValueShare is not 0m => HolderHorizonBand.Unknown,
            < ShortHorizonBelowQuarters => HolderHorizonBand.Short,
            _ => HolderHorizonBand.Medium,
        };
}
