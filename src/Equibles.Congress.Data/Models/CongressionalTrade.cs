using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data.Models;

[Index(nameof(CommonStockId), nameof(TransactionDate))]
[Index(nameof(CongressMemberId), nameof(TransactionDate))]
// The trade identity / upsert key (see CongressionalTradeSyncService.PersistTrades). OwnerType
// and the amount bracket are part of a trade's identity: a member can file two same-day
// purchases of the same stock that differ only in bracket or in who holds them (self vs.
// spouse vs. dependent child), and without those columns the second one is silently dropped.
// FilingDate is deliberately EXCLUDED — the disclosure feeds re-date the same filing between
// scrapes, so keying on it would re-insert existing trades as duplicates.
[Index(
    nameof(CommonStockId),
    nameof(CongressMemberId),
    nameof(TransactionDate),
    nameof(TransactionType),
    nameof(AssetName),
    nameof(OwnerType),
    nameof(AmountFrom),
    nameof(AmountTo),
    IsUnique = true
)]
[Index(nameof(FilingDate))]
[Index(nameof(TransactionDate))]
public class CongressionalTrade
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CongressMemberId { get; set; }
    public virtual CongressMember CongressMember { get; set; }

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateOnly TransactionDate { get; set; }
    public DateOnly FilingDate { get; set; }

    public CongressTransactionType TransactionType { get; set; }

    // Not-null with a '' default (see CongressModuleConfiguration): OwnerType is part of the
    // unique key above, and Postgres treats NULLs as distinct in unique indexes — a nullable
    // column here would silently disable dedup for every trade without an owner annotation.
    [Required]
    [MaxLength(64)]
    public string OwnerType { get; set; }

    [Required]
    [MaxLength(256)]
    public string AssetName { get; set; }

    public long AmountFrom { get; set; }
    public long AmountTo { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
