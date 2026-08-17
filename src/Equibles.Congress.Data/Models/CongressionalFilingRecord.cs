using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data.Models;

/// <summary>
/// Marks one source filing as fully ingested so sync cycles never re-download
/// it. A row is written only after the filing was fetched, parsed and its data
/// committed (or it was skipped by a deterministic policy, e.g. a scanned
/// paper filing) — fetch and parse failures are never recorded, so those
/// filings keep retrying until they succeed. Deleting rows forces a re-ingest
/// of the matching filings on the next cycle.
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

    // Transactions or schedule lines the filing yielded; 0 also covers
    // policy-skipped filings (scanned paper reports, candidate reports).
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
