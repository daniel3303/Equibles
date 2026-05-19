using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Data.Models;

/// <summary>
/// Catalog of XBRL concepts (taxonomy + tag). Deduplicates concept metadata so
/// it is not repeated across the millions of <see cref="FinancialFact"/> rows
/// that reference it.
/// </summary>
[Index(nameof(Taxonomy), nameof(Tag), IsUnique = true)]
public class FinancialConcept
{
    // Client-generated Guid key. Without DatabaseGeneratedOption.None EF marks
    // it store-generated, so FlexLabs UpsertRange omits it from the INSERT and
    // Postgres (no column default) rejects the row with a NOT NULL violation.
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public FactTaxonomy Taxonomy { get; set; }

    /// <summary>Concept tag, e.g. <c>Revenues</c>, <c>NetIncomeLoss</c>.</summary>
    [Required]
    [MaxLength(256)]
    public string Tag { get; set; }

    /// <summary>SEC human-readable label for the concept; null when absent.</summary>
    [MaxLength(512)]
    public string Label { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

    public virtual List<FinancialFact> Facts { get; set; } = [];
}
