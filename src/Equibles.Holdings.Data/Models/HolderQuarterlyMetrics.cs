using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// A 13F filer's book measured for one closed quarter from its common-share positions, with the
/// style bands read from those measurements. Put, call and principal legs are kept apart in their
/// own value columns and never weigh in the equity measurements.
/// </summary>
[Index(nameof(InstitutionalHolderId), nameof(ReportDate), IsUnique = true)]
[Index(nameof(ReportDate), nameof(StyleBand))]
public class HolderQuarterlyMetrics
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitutionalHolderId { get; set; }
    public virtual InstitutionalHolder InstitutionalHolder { get; set; }
    public DateOnly ReportDate { get; set; }

    /// <summary>Valued common-share positions; a pending or unavailable value adds nothing.</summary>
    public long EquityAum { get; set; }

    /// <summary>Null when the filer filed no 13F the quarter before.</summary>
    public long? PreviousEquityAum { get; set; }

    public int StockCount { get; set; }

    [Precision(20, 2)]
    public decimal? BuyValue { get; set; }

    [Precision(20, 2)]
    public decimal? SellValue { get; set; }

    [Precision(12, 6)]
    public decimal? Turnover { get; set; }

    [Precision(12, 6)]
    public decimal? TrailingAnnualTurnover { get; set; }

    public int? NewCount { get; set; }
    public int? ExitCount { get; set; }
    public int? IncreasedCount { get; set; }
    public int? ReducedCount { get; set; }
    public int? UnpricedTradeCount { get; set; }

    [Precision(12, 6)]
    public decimal? TopTenWeight { get; set; }

    [Precision(12, 6)]
    public decimal? Herfindahl { get; set; }

    [Precision(14, 4)]
    public decimal? EffectivePositions { get; set; }

    [Precision(10, 4)]
    public decimal? ValueWeightedTenureQuarters { get; set; }

    [Precision(12, 6)]
    public decimal? CensoredValueShare { get; set; }

    /// <summary>Active share against every 13F filer's combined common-share book that quarter.</summary>
    [Precision(12, 6)]
    public decimal? AggregateActiveShare { get; set; }

    /// <summary>
    /// The benchmark the book is closest to by active share, among the published indexes and the
    /// combined 13F book measured that quarter (<see cref="HolderQuarterlyBenchmarkActiveShare"/>).
    /// </summary>
    [MaxLength(64)]
    public string ClosestBenchmark { get; set; }

    [MaxLength(128)]
    public string ClosestBenchmarkName { get; set; }

    public DateOnly? ClosestBenchmarkAsOf { get; set; }

    [Precision(12, 6)]
    public decimal? ClosestActiveShare { get; set; }

    /// <summary>
    /// Half the summed absolute differences between the book's sector weights and the combined
    /// 13F book's; an issuer without a sector weighs in as Unclassified on both sides.
    /// </summary>
    [Precision(12, 6)]
    public decimal? SectorActiveShare { get; set; }

    /// <summary>
    /// The filer whose previous-quarter book the trades compare with: the filer itself, or the one
    /// predecessor whose 13F reports it alone continues from this quarter; null without a baseline.
    /// </summary>
    public Guid? ComparedHolderId { get; set; }

    /// <summary>
    /// Net assets of the index series and of every registered fund series the filer's adviser
    /// manages, by each series' latest N-CEN report for a period ending up to the quarter end.
    /// </summary>
    public decimal? IndexSeriesNetAssets { get; set; }
    public decimal? AdvisedSeriesNetAssets { get; set; }

    /// <summary>The latest N-CEN period behind the advised series figures.</summary>
    public DateOnly? AdvisedSeriesAsOf { get; set; }

    public long CallValue { get; set; }
    public long PutValue { get; set; }
    public long PrincipalValue { get; set; }

    public HolderStyleBand StyleBand { get; set; }
    public HolderIndexLikeEvidence IndexLikeEvidence { get; set; }
    public HolderTurnoverBand TurnoverBand { get; set; }
    public HolderHorizonBand HorizonBand { get; set; }
    public int BandVersion { get; set; }
    public int CalculationVersion { get; set; }
    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}
