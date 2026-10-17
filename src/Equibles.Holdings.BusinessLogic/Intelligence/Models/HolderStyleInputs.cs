namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>A filer's measurements in one quarter that its style bands are read from.</summary>
public sealed record HolderStyleInputs
{
    public long EquityAum { get; init; }
    public int StockCount { get; init; }

    /// <summary>The lowest active share across the benchmarks measured for the quarter.</summary>
    public decimal? ClosestActiveShare { get; init; }

    public decimal? TrailingAnnualTurnover { get; init; }
    public decimal? TopTenWeight { get; init; }
    public decimal? EffectivePositions { get; init; }
    public decimal? ValueWeightedTenureQuarters { get; init; }

    /// <summary>Net assets of the index series the filer advises, by their latest N-CEN.</summary>
    public decimal? IndexSeriesNetAssets { get; init; }

    /// <summary>Net assets of every registered fund series the filer advises.</summary>
    public decimal? AdvisedSeriesNetAssets { get; init; }
}
