using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

[Index(nameof(CommonStockId), nameof(ReportDate))]
[Index(nameof(InstitutionalHolderId), nameof(ReportDate))]
[Index(nameof(AccessionNumber))]
// Unique index configured via Fluent API in EquiblesFinancialDbContext with NULLS NOT DISTINCT.
// A second covering index on (CommonStockId, ReportDate) INCLUDE
// (InstitutionalHolderId, Value, Shares) is configured there too — Postgres-only
// `INCLUDE` clauses aren't expressible via the [Index] attribute, so the Fluent
// API call carries it. The covering index lets the ownership-trend GROUP BY on
// /Stocks/{ticker}/Holdings run as an index-only scan.
[Index(nameof(FilingDate))]
[Index(nameof(ReportDate))]
[Index(nameof(FilingType))]
public class InstitutionalHolding
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InstitutionalHolderId { get; set; }
    public virtual InstitutionalHolder InstitutionalHolder { get; set; }

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public DateOnly FilingDate { get; set; }
    public DateOnly ReportDate { get; set; }

    public long Value { get; set; }

    /// <summary>
    /// The position's market value exactly as the filer reported it, normalised to dollars.
    /// Null when the filing reports no value at all (Schedule 13D/G) or filed a non-positive one.
    /// </summary>
    /// <remarks>
    /// <see cref="Value"/> stays the derived figure — it is the one comparable across filings,
    /// since 13D/G positions have no filed value and a filer's own mark can be stale. This column
    /// exists so the derivation can be audited against its source instead of trusted blindly: a
    /// gross disagreement between the two is the signature of a units error (a missing split, a
    /// depositary ratio) that is otherwise invisible once the filing has been parsed and discarded.
    /// </remarks>
    public long? FiledValue { get; set; }

    public long Shares { get; set; }
    public ShareType ShareType { get; set; }
    public OptionType? OptionType { get; set; }
    public InvestmentDiscretion InvestmentDiscretion { get; set; }
    public FilingType FilingType { get; set; }

    public long VotingAuthSole { get; set; }
    public long VotingAuthShared { get; set; }
    public long VotingAuthNone { get; set; }

    // Percent of the class beneficially owned. Reported on Schedule 13D/13G cover
    // pages; null for Form 13F (which has no percent-of-class concept).
    [Column(TypeName = "numeric(7,4)")]
    public decimal? PercentOfClass { get; set; }

    // 13D/G cover pages report fully spelled-out class descriptions that can far
    // exceed the short titles 13F info tables carry, so the column is wider than
    // the other identifier fields.
    [MaxLength(512)]
    public string TitleOfClass { get; set; }

    [MaxLength(9)]
    public string Cusip { get; set; }

    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    public bool IsAmendment { get; set; }
    public bool ValuePending { get; set; }
    public int ValueRetryCount { get; set; }
    public DateTime? ValueLastRetryAt { get; set; }

    /// <summary>
    /// The position's dollar value could not be derived honestly, so <see cref="Value"/> is zero
    /// and means "unknown" rather than "nothing". Set when the reported share count is larger than
    /// the issuer itself — the tell of a count in different units from our price series, such as a
    /// depositary-share issuer whose filer reports the underlying ordinary shares. The share count
    /// is still what the filer reported; only the valuation is withheld.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="ValuePending"/>, which means "no price yet, retry later". This one
    /// never resolves on its own: re-deriving from the same count would reproduce the same wrong
    /// figure, so the repricing lane must leave these rows alone.
    /// </remarks>
    public bool ValueUnavailable { get; set; }

    public List<HoldingManagerEntry> ManagerEntries { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
