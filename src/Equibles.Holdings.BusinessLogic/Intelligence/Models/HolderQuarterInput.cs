namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>Everything one filer's metrics for one quarter are computed from.</summary>
public sealed record HolderQuarterInput
{
    public Guid InstitutionalHolderId { get; init; }
    public DateOnly ReportDate { get; init; }
    public IReadOnlyList<HolderIntelligenceRow> Current { get; init; } = [];

    /// <summary>The quarter before's rows, or null when the filer filed no 13F then.</summary>
    public IReadOnlyList<HolderIntelligenceRow> Previous { get; init; }

    /// <summary>
    /// The factor that puts a previous-quarter share count on this quarter's basis, per security;
    /// a missing key is 1 and a null factor means the split basis could not be resolved.
    /// </summary>
    public IReadOnlyDictionary<HolderSecurityKey, decimal?> PreviousShareFactors { get; init; } =
        new Dictionary<HolderSecurityKey, decimal?>();

    /// <summary>
    /// Each security's quarter average price on this quarter's share basis; a missing key prices
    /// its trades at the quarter-end value per share.
    /// </summary>
    public IReadOnlyDictionary<HolderSecurityKey, decimal> AveragePrices { get; init; } =
        new Dictionary<HolderSecurityKey, decimal>();

    /// <summary>The filer's stored presence masks, per security.</summary>
    public IReadOnlyDictionary<HolderSecurityKey, long> Presence { get; init; } =
        new Dictionary<HolderSecurityKey, long>();

    /// <summary>The quarters the filer filed a 13F, as a presence-style bitmask.</summary>
    public long FiledMask { get; init; }

    /// <summary>The bit of the corpus's first complete quarter, where tenure is censored.</summary>
    public int FirstCoveredQuarter { get; init; }

    /// <summary>Every 13F filer's combined common-share value per issuer this quarter.</summary>
    public IReadOnlyDictionary<Guid, decimal> AggregateBenchmark { get; init; } =
        new Dictionary<Guid, decimal>();

    /// <summary>The three quarters before's stored turnover, oldest first; null where unknown.</summary>
    public IReadOnlyList<decimal?> PriorTurnovers { get; init; } = [null, null, null];

    /// <summary>
    /// The filer's adviser's index and total registered fund series net assets, by each series'
    /// latest N-CEN report for a period ending up to the quarter end; null when none is known.
    /// </summary>
    public decimal? IndexSeriesNetAssets { get; init; }

    public decimal? AdvisedSeriesNetAssets { get; init; }

    /// <summary>The latest N-CEN period behind the advised series figures.</summary>
    public DateOnly? AdvisedSeriesAsOf { get; init; }
}
