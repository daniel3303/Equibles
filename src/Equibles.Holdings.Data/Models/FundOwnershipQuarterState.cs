using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// The fund-ownership drain's state for one closed quarter: the version and input fingerprint its
/// published <see cref="StockQuarterlyFundOwnership"/> and <see cref="HolderStockAdvisedFundShares"/>
/// rows were computed from. A quarter is rebuilt when either differs from the current ones.
/// </summary>
[Index(nameof(ReportDate), IsUnique = true)]
public class FundOwnershipQuarterState
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateOnly ReportDate { get; set; }

    /// <summary>The N-PORT, N-CEN, fund-link and 13F state the published rows were built from.</summary>
    [MaxLength(256)]
    public string InputFingerprint { get; set; }

    /// <summary>When the rows were last published; null while the quarter has never been.</summary>
    public DateTime? ComputedAt { get; set; }

    public int CalculationVersion { get; set; }

    /// <summary>Series whose report the published rows count.</summary>
    public int SeriesCount { get; set; }

    /// <summary>Of <see cref="SeriesCount"/>, the series no N-CEN fund report classifies.</summary>
    public int UnclassifiedSeriesCount { get; set; }

    public DateTime? AttemptedAt { get; set; }

    public int FailureCount { get; set; }

    public string LastError { get; set; }
}
