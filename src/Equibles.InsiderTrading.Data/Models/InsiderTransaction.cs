using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Data.Models;

[Index(nameof(CommonStockId), nameof(TransactionDate))]
[Index(nameof(InsiderOwnerId), nameof(TransactionDate))]
[Index(nameof(AccessionNumber), nameof(TransactionOrder), IsUnique = true)]
[Index(nameof(FilingDate))]
[Index(nameof(TransactionDate))]
[Index(nameof(IsPriceValid), nameof(TransactionDate))]
public class InsiderTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InsiderOwnerId { get; set; }
    public virtual InsiderOwner InsiderOwner { get; set; }

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateOnly FilingDate { get; set; }
    public DateOnly TransactionDate { get; set; }

    public TransactionCode TransactionCode { get; set; }

    public long Shares { get; set; }
    public decimal PricePerShare { get; set; }
    public AcquiredDisposed AcquiredDisposed { get; set; }
    public long SharesOwnedAfter { get; set; }
    public OwnershipNature OwnershipNature { get; set; }

    [MaxLength(128)]
    public string SecurityTitle { get; set; }

    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    /// <summary>
    /// Ordinal position of this row within its Form 3/4 filing (0-based). Form 4 XML
    /// has no per-transaction identifier — uniqueness is by (AccessionNumber, position).
    /// </summary>
    public int TransactionOrder { get; set; }

    public bool IsAmendment { get; set; }

    /// <summary>
    /// False when the filer typed a bogus value into transactionPricePerShare —
    /// most commonly the total transaction value instead of the per-share price.
    /// Detected by cross-checking against the Yahoo unadjusted close on
    /// TransactionDate. Dashboards filter on this so a single fat-fingered Form 4
    /// doesn't drown out every other row when sorted by Shares × PricePerShare.
    /// Default true — only rows we positively reject are flipped to false.
    /// </summary>
    public bool IsPriceValid { get; set; } = true;

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
