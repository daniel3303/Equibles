using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// A 13F filer's common-share buying and selling in one sector between a closed quarter and the
/// one before, priced as its <see cref="HolderQuarterlyMetrics"/> trades are. Only sectors it
/// traded have a row; the rows sum to the metrics' buy and sell values up to rounding.
/// </summary>
[Index(nameof(ReportDate))]
public class HolderQuarterlySectorFlow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitutionalHolderId { get; set; }
    public virtual InstitutionalHolder InstitutionalHolder { get; set; }
    public DateOnly ReportDate { get; set; }
    public HolderFlowSectorGroup SectorGroup { get; set; }

    /// <summary>The issuer's sector when it was computed; null outside the Sector group.</summary>
    public Guid? SectorId { get; set; }

    [Precision(20, 2)]
    public decimal BuyValue { get; set; }

    [Precision(20, 2)]
    public decimal SellValue { get; set; }
}
