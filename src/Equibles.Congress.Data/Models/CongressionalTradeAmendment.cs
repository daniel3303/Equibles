using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data.Models;

/// <summary>
/// One amended or deleted row of an amending House PTR, kept with its filed facts so the row it
/// supersedes can be found whichever filing is read first. A matched original is deleted from
/// <see cref="CongressionalTrade"/> and never re-inserted by a replay, so no trade reader filters.
/// </summary>
[Index(
    nameof(FilingKind),
    nameof(SourceId),
    nameof(SourceRowIndex),
    IsUnique = true,
    Name = "IX_CongressionalTradeAmendment_SourceRow"
)]
[Index(
    nameof(SupersededSourceId),
    nameof(SupersededSourceRowIndex),
    Name = "IX_CongressionalTradeAmendment_Superseded"
)]
[Index(nameof(Verdict))]
[Index(nameof(CongressMemberId))]
public class CongressionalTradeAmendment
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public CongressionalFilingKind FilingKind { get; set; }

    [Required]
    [MaxLength(128)]
    public string SourceId { get; set; }

    public int SourceRowIndex { get; set; }

    public Guid CongressMemberId { get; set; }
    public virtual CongressMember CongressMember { get; set; }

    public CongressionalTradeFilingStatus Status { get; set; }

    public DateOnly FilingDate { get; set; }
    public DateOnly TransactionDate { get; set; }
    public CongressTransactionType TransactionType { get; set; }

    [Required]
    [MaxLength(64)]
    public string OwnerType { get; set; }

    [Required]
    [MaxLength(256)]
    public string AssetName { get; set; }

    [Required]
    [MaxLength(128)]
    public string AssetType { get; set; } = "";

    [Required]
    [MaxLength(32)]
    public string FiledTicker { get; set; } = "";

    [Required]
    [MaxLength(256)]
    public string Subholding { get; set; } = "";

    public long AmountFrom { get; set; }
    public long AmountTo { get; set; }

    public CongressionalTradeAmendmentVerdict Verdict { get; set; }

    /// <summary>The superseded row's source key, in the same filing kind; set once matched.</summary>
    [MaxLength(128)]
    public string SupersededSourceId { get; set; }

    public int? SupersededSourceRowIndex { get; set; }

    /// <summary>
    /// The superseded row's filing date. An amended row keeps it as its own filing date, so a
    /// correction never makes a trade that was disclosed on time look late.
    /// </summary>
    public DateOnly? FirstDisclosedDate { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
