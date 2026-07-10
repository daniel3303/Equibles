using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// A filing whose ingest failed deterministically (normalization threw), so
/// nothing was persisted. Without this row the scraper re-downloaded the
/// multi-MB submission on every enumeration forever — the audited "poison
/// cohort" (2,466 filings burning ~25k EDGAR calls/day under the legacy
/// sweep). The row schedules retries on an exponential backoff instead of
/// abandoning the filing: it keeps retrying forever (capped at the max
/// interval), so a later parser fix still ingests it and no filing is ever
/// permanently lost. Deleted when the filing finally ingests.
/// </summary>
[Index(nameof(NextRetryAt))]
public class FailedFilingIngest
{
    /// <summary>Accession number with dashes — globally unique in EDGAR.</summary>
    [Key]
    [MaxLength(30)]
    public string AccessionNumber { get; set; }

    [Required]
    [MaxLength(20)]
    public string Cik { get; set; }

    [MaxLength(50)]
    public string FormType { get; set; }

    public DateOnly FilingDate { get; set; }

    public int AttemptCount { get; set; }

    public DateTime LastAttemptAt { get; set; }

    /// <summary>Precomputed so the scraper's skip filter is one indexed range check.</summary>
    public DateTime NextRetryAt { get; set; }

    [MaxLength(2000)]
    public string LastError { get; set; }
}
