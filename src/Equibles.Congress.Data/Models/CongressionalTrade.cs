using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data.Models;

[Index(nameof(CommonStockId), nameof(TransactionDate))]
[Index(nameof(CongressMemberId), nameof(TransactionDate))]
// Legacy semantic lookup used only while parser-v5 replay adopts rows written before stable
// source identity. It is intentionally non-unique: the source row key below owns dedup now.
[Index(
    nameof(CommonStockId),
    nameof(CongressMemberId),
    nameof(TransactionDate),
    nameof(TransactionType),
    nameof(AssetName),
    nameof(OwnerType),
    nameof(AmountFrom),
    nameof(AmountTo),
    nameof(AssetType),
    nameof(Subholding),
    Name = "IX_CongressionalTrade_LegacyFilingIdentity"
)]
[Index(nameof(FilingKind), nameof(SourceId), nameof(SourceRowIndex), IsUnique = true)]
[Index(nameof(FilingDate))]
[Index(nameof(TransactionDate))]
public class CongressionalTrade
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CongressMemberId { get; set; }
    public virtual CongressMember CongressMember { get; set; }

    // Derived from the immutable filed ticker and authoritative dated issuer evidence. Null is
    // preferable to attaching a reused symbol to the wrong company.
    public Guid? CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    /// <summary>The ticker exactly as normalized from the congressional filing.</summary>
    [Required]
    [MaxLength(32)]
    public string FiledTicker { get; set; } = "";

    /// <summary>
    /// Stable source-row identity. Legacy rows remain null until an authoritative source replay
    /// adopts them; PostgreSQL permits multiple nulls in this unique index.
    /// </summary>
    public CongressionalFilingKind? FilingKind { get; set; }

    [MaxLength(128)]
    public string SourceId { get; set; }

    public int? SourceRowIndex { get; set; }

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

    // The authoritative filed type: House abbreviation (ST/OP/...) or Senate label.
    // Empty only on rows written before the trade parser began retaining it.
    [Required]
    [MaxLength(128)]
    public string AssetType { get; set; } = "";

    // The filed parent account/subholding. This is separate from OwnerType: two assets may both
    // belong to the spouse while sitting under different disclosed brokerage/retirement accounts.
    [Required]
    [MaxLength(256)]
    public string Subholding { get; set; } = "";

    public long AmountFrom { get; set; }
    public long AmountTo { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
