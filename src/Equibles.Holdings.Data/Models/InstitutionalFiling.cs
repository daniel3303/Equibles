using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// One row per 13F-HR submission, keyed by accession number — the filing-level
/// rollup of the per-position <see cref="InstitutionalHolding"/> rows that share
/// that accession. Holdings are stored only at the position grain, so the
/// "latest filings" feed used to reconstruct one row per filing with a
/// table-wide GROUP BY on every request; this table is that rollup, written once
/// at ingestion and read back with a plain indexed scan.
///
/// <para>
/// <see cref="PositionCount"/> and <see cref="TotalValue"/> count only the
/// <em>tracked</em> positions that were actually imported (untracked CUSIPs are
/// skipped during import), matching what the old GROUP BY over
/// <see cref="InstitutionalHolding"/> produced.
/// </para>
///
/// <para>
/// Each accession is its own row. A RESTATEMENT amendment carries a new
/// accession and deletes+reinserts the holdings under it, so the original and
/// the amendment remain distinct filing rows — exactly as the per-accession
/// GROUP BY treated them.
/// </para>
/// </summary>
[Index(nameof(AccessionNumber), IsUnique = true)]
// Composite over (FilingDate, AccessionNumber) so the "latest filings" feed's
// ORDER BY FilingDate DESC, AccessionNumber DESC + page is served by a backward
// index scan instead of a full sort of the whole rollup, which exceeded the 30s
// command timeout and 500'd the page on every load (#3565). FilingDate is the
// leading column, so this also serves the FilingDate-only lookups the old
// single-column index covered.
[Index(nameof(FilingDate), nameof(AccessionNumber))]
public class InstitutionalFiling
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    public Guid InstitutionalHolderId { get; set; }
    public virtual InstitutionalHolder InstitutionalHolder { get; set; }

    public DateOnly FilingDate { get; set; }
    public DateOnly ReportDate { get; set; }
    public bool IsAmendment { get; set; }

    public int PositionCount { get; set; }
    public long TotalValue { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
