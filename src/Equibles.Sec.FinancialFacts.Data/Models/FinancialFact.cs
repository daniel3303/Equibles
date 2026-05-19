using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Data.Models;

/// <summary>
/// A single structured financial fact (one concept, one period, one unit) for a
/// company, sourced from SEC's Company Facts API. Restatements are retained as
/// separate rows discriminated by <see cref="AccessionNumber"/>; the
/// most-recently-reported value for a period is the one with the latest
/// <see cref="FiledDate"/>.
/// </summary>
[Index(nameof(CommonStockId), nameof(FinancialConceptId), nameof(PeriodEnd))]
[Index(nameof(CommonStockId), nameof(FiscalYear), nameof(FiscalPeriod))]
[Index(nameof(DocumentId))]
[Index(
    nameof(CommonStockId),
    nameof(FinancialConceptId),
    nameof(Unit),
    nameof(PeriodStart),
    nameof(PeriodEnd),
    nameof(AccessionNumber),
    IsUnique = true
)]
public class FinancialFact
{
    // Client-generated Guid key. Without DatabaseGeneratedOption.None EF marks
    // it store-generated, so FlexLabs UpsertRange omits it from the INSERT and
    // Postgres (no column default) rejects the row with a NOT NULL violation.
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public Guid FinancialConceptId { get; set; }
    public virtual FinancialConcept FinancialConcept { get; set; }

    /// <summary>
    /// Source filing, best-effort linked by accession number. Null when the
    /// filing was never ingested as a <see cref="Document"/> (the Company Facts
    /// API spans every XBRL filing; only configured doc types are stored).
    /// </summary>
    public Guid? DocumentId { get; set; }
    public virtual Document Document { get; set; }

    /// <summary>XBRL unit, e.g. <c>USD</c>, <c>USD/shares</c>, <c>shares</c>, <c>pure</c>.</summary>
    [Required]
    [MaxLength(32)]
    public string Unit { get; set; }

    public FactPeriodType PeriodType { get; set; }

    /// <summary>
    /// Period start. For <see cref="FactPeriodType.Instant"/> facts this equals
    /// <see cref="PeriodEnd"/> so the unique index stays NULL-free (Postgres
    /// treats NULLs as distinct in unique indexes).
    /// </summary>
    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    /// <summary>
    /// Reported value. Unbounded <c>numeric</c> — handles both multi-trillion
    /// totals and fractional per-share amounts without precision loss.
    /// </summary>
    [Column(TypeName = "numeric")]
    public decimal Value { get; set; }

    public int FiscalYear { get; set; }

    public SecFiscalPeriod FiscalPeriod { get; set; }

    /// <summary>Source form (10-K, 10-Q, 20-F, …). Reuses the SEC module's DocumentType.</summary>
    [Required]
    public DocumentType Form { get; set; }

    public DateOnly FiledDate { get; set; }

    [Required]
    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    /// <summary>SEC standardized frame label, e.g. <c>CY2024Q1I</c>; null if absent.</summary>
    [MaxLength(32)]
    public string Frame { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
