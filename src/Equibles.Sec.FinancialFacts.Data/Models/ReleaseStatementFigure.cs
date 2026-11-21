using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Data.Models;

/// <summary>
/// A statement figure an earnings release states for a period no periodic report covered when
/// the release was read. Kept apart from <see cref="FinancialFact"/> on purpose: a release
/// figure would be the only fact of its period, so every reader of facts (trailing twelve
/// months, multiples, screens) would take it by default. A surface shows these only by asking.
/// </summary>
[Index(nameof(EquityIssuerId), nameof(PeriodEnd))]
[Index(
    nameof(ReleaseStatementReadId),
    nameof(FinancialConceptId),
    nameof(PeriodStart),
    nameof(PeriodEnd),
    IsUnique = true
)]
public class ReleaseStatementFigure
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReleaseStatementReadId { get; set; }
    public virtual ReleaseStatementRead Read { get; set; }

    public Guid EquityIssuerId { get; set; }

    public Guid FinancialConceptId { get; set; }
    public virtual FinancialConcept FinancialConcept { get; set; }

    /// <summary>The release's 8-K, repeated from the read so a figure cites it without a join.</summary>
    public Guid DocumentId { get; set; }

    public DateOnly FiledDate { get; set; }

    [Required]
    [MaxLength(32)]
    public string Unit { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    [Column(TypeName = "numeric")]
    public decimal Value { get; set; }

    public int FiscalYear { get; set; }

    public SecFiscalPeriod FiscalPeriod { get; set; }

    /// <summary>Line of the release text the figure was read from (1-based).</summary>
    public int SourceLine { get; set; }

    /// <summary>The release's own label for the row, shown beside the figure.</summary>
    [Required]
    [MaxLength(512)]
    public string SourceLabel { get; set; }

    [NotMapped]
    public FactPeriodType PeriodType =>
        PeriodStart == PeriodEnd ? FactPeriodType.Instant : FactPeriodType.Duration;
}
