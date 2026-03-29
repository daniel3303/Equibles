using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

[Index(nameof(CommonStockId), nameof(ReportDate))]
[Index(nameof(InstitutionalHolderId), nameof(ReportDate))]
[Index(nameof(AccessionNumber))]
// Unique index configured via Fluent API in EquiblesDbContext with NULLS NOT DISTINCT
[Index(nameof(FilingDate))]
[Index(nameof(ReportDate))]
public class InstitutionalHolding {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InstitutionalHolderId { get; set; }
    public virtual InstitutionalHolder InstitutionalHolder { get; set; }

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateOnly FilingDate { get; set; }
    public DateOnly ReportDate { get; set; }

    public long Value { get; set; }
    public long Shares { get; set; }
    public ShareType ShareType { get; set; }
    public OptionType? OptionType { get; set; }
    public InvestmentDiscretion InvestmentDiscretion { get; set; }

    public long VotingAuthSole { get; set; }
    public long VotingAuthShared { get; set; }
    public long VotingAuthNone { get; set; }

    [MaxLength(128)]
    public string TitleOfClass { get; set; }

    [MaxLength(9)]
    public string Cusip { get; set; }

    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    public bool IsAmendment { get; set; }
    public bool ValuePending { get; set; }

    public List<HoldingManagerEntry> ManagerEntries { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
