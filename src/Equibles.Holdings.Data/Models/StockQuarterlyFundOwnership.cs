using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// Registered funds' long share positions in one listed security at a closed quarter end, split by
/// the fund type each series reports on Form N-CEN (<see cref="FundOwnershipBucket"/>). Each series
/// counts its latest Form NPORT-P whose portfolio date falls inside the calendar quarter, and its
/// balance is restated onto the quarter end's split basis for that listing. Fund positions sit
/// inside their advisers' 13F reports, so these rows are never added to 13F totals.
/// </summary>
[Index(nameof(ReportDate))]
public class StockQuarterlyFundOwnership
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EquityIssuerId { get; set; }

    /// <summary>Null for the issuer's primary listing, as on the 13F holding row.</summary>
    [MaxLength(32)]
    public string ListedTicker { get; set; }

    public DateOnly ReportDate { get; set; }

    public FundOwnershipBucket Bucket { get; set; }

    /// <summary>Shares on the quarter end's split basis, including <see cref="SplitUnresolvedShares"/> as filed.</summary>
    public long Shares { get; set; }

    /// <summary>Shares from series whose report predates a split that cannot be attributed, kept as filed.</summary>
    public long SplitUnresolvedShares { get; set; }

    /// <summary>The positions' value in US dollars as each fund filed it, at its own report date.</summary>
    [Precision(20, 2)]
    public decimal Value { get; set; }

    public int SeriesCount { get; set; }

    /// <summary>The oldest portfolio date among the series counted.</summary>
    public DateOnly OldestReportPeriod { get; set; }

    /// <summary>The newest portfolio date among the series counted.</summary>
    public DateOnly NewestReportPeriod { get; set; }
}
