using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Data.Models;

[Index(nameof(CommonStockId), nameof(TransactionDate))]
[Index(nameof(InsiderOwnerId), nameof(TransactionDate))]
[Index(nameof(AccessionNumber))]
[Index(nameof(CommonStockId), nameof(InsiderOwnerId), nameof(TransactionDate),
    nameof(TransactionCode), nameof(SecurityTitle), IsUnique = true)]
[Index(nameof(FilingDate))]
[Index(nameof(TransactionDate))]
public class InsiderTransaction {
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

    public bool IsAmendment { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
