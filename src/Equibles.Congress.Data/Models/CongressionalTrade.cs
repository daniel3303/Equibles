using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data.Models;

[Index(nameof(CommonStockId), nameof(TransactionDate))]
[Index(nameof(CongressMemberId), nameof(TransactionDate))]
[Index(nameof(CommonStockId), nameof(CongressMemberId), nameof(TransactionDate),
    nameof(TransactionType), nameof(AssetName), IsUnique = true)]
[Index(nameof(FilingDate))]
[Index(nameof(TransactionDate))]
public class CongressionalTrade {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CongressMemberId { get; set; }
    public virtual CongressMember CongressMember { get; set; }

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateOnly TransactionDate { get; set; }
    public DateOnly FilingDate { get; set; }

    public CongressTransactionType TransactionType { get; set; }

    [MaxLength(64)]
    public string OwnerType { get; set; }

    [Required]
    [MaxLength(256)]
    public string AssetName { get; set; }

    public long AmountFrom { get; set; }
    public long AmountTo { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
