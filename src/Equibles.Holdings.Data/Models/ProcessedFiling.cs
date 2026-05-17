using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// Records every individual 13F-HR submission the real-time ingestion path has
/// already handed to the import pipeline, keyed by accession number.
///
/// This is the dedup ledger that makes re-sweeping the daily index safe: an
/// amendment carries a <em>new</em> accession, so it is still processed, but a
/// previously-handled original is never re-processed — without this, a later
/// sweep of an original (whose holdings rows were deleted by its own
/// amendment's delete-by-period) would upsert stale holdings back over the
/// amendment. Filings that produced no tracked holdings are recorded too, so
/// they are not re-downloaded every cycle.
/// </summary>
[Index(nameof(AccessionNumber), IsUnique = true)]
public class ProcessedFiling
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
