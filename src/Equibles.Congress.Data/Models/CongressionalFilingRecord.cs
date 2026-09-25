using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data.Models;

/// <summary>
/// Marks one source filing as fully ingested so sync cycles never re-download
/// it. A row is written only after the filing was fetched, parsed and its data
/// committed. Fetch failures (a missing or unreadable file) are never recorded,
/// so those filings keep retrying; every verdict a parser reaches on readable
/// bytes is deterministic (a scanned paper filing, rows it cannot read, an
/// exchange it skips by policy) and IS recorded at the parser version that
/// reached it, because re-reading the same bytes with the same parser cannot
/// change the answer. A parser version bump reopens them, as does deleting rows.
/// </summary>
[Index(nameof(Kind), nameof(SourceId), IsUnique = true)]
public class CongressionalFilingRecord
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public CongressionalFilingKind Kind { get; set; }

    // House: the DocID from the yearly FD index; Senate: the eFD report GUID.
    [Required]
    [MaxLength(128)]
    public string SourceId { get; set; }

    public DateOnly FilingDate { get; set; }

    // Transactions or schedule lines the filing yielded; 0 also covers policy-skipped
    // filings (scanned paper reports, candidate reports, exchange-only reports) and a
    // readable filing whose rows the parser of that version could not read.
    public int ItemCount { get; set; }

    /// <summary>
    /// The parser generation that produced this row's data. A lane that starts
    /// extracting new fields raises its current version, which demotes every
    /// older row to "not yet ingested" so the filing is re-fetched and re-parsed
    /// — the same effect as deleting the row, without a manual sweep. Lanes that
    /// have never bumped it stay at 0 and are unaffected.
    /// </summary>
    public int ParserVersion { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
