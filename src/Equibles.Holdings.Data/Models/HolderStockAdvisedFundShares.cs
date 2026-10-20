using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// The part of a 13F filer's position in one listed security at a closed quarter end that its
/// registered funds hold: the series linked to the filer through their sole investment adviser
/// (<see cref="FundSeriesHolderLink"/>), counted as <see cref="StockQuarterlyFundOwnership"/> counts
/// them and capped at the filer's 13F common shares. A floor: sub-advised and multi-adviser series,
/// separate accounts and private funds are never counted.
/// </summary>
[Index(nameof(EquityIssuerId), nameof(ReportDate))]
[Index(nameof(ReportDate))]
public class HolderStockAdvisedFundShares
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InstitutionalHolderId { get; set; }
    public virtual InstitutionalHolder InstitutionalHolder { get; set; }

    public Guid EquityIssuerId { get; set; }

    /// <summary>Null for the issuer's primary listing, as on the 13F holding row.</summary>
    [MaxLength(32)]
    public string ListedTicker { get; set; }

    public DateOnly ReportDate { get; set; }

    /// <summary>The filer's 13F common shares in the listing for the quarter.</summary>
    public long HolderShares { get; set; }

    /// <summary>Shares its linked registered funds hold, at most <see cref="HolderShares"/>.</summary>
    public long FundShares { get; set; }

    /// <summary>The part of <see cref="FundShares"/> its index funds and index ETFs hold.</summary>
    public long IndexFundShares { get; set; }

    public int SeriesCount { get; set; }

    public int IndexSeriesCount { get; set; }

    /// <summary>True when the funds' shares exceeded the 13F position and were cut to it.</summary>
    public bool IsCapped { get; set; }
}
