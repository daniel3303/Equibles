using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.InsiderTrading.Data.Models;

/// <summary>
/// One row per SEC Form 3/4/5 filing, holding the gzip-compressed raw ownership
/// XML exactly as parsed. Keyed by <see cref="AccessionNumber"/> (the same key
/// the transactions carry), so the filing's transactions can be re-derived from
/// the stored XML without re-fetching from EDGAR.
///
/// This is what makes reprocessing cheap: when the parser changes (a bumped
/// <see cref="InsiderTransaction.CurrentParserVersion"/>), stale transactions
/// are re-parsed from the cached XML here — a local read, not a rate-limited
/// network crawl.
/// </summary>
[Index(nameof(AccessionNumber), IsUnique = true)]
[Index(nameof(CaptureStatus))]
public class InsiderFiling
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// SEC accession number of the filing (globally unique, e.g.
    /// 0000320193-24-000123). Matches <see cref="InsiderTransaction.AccessionNumber"/>.
    /// </summary>
    [Required]
    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    /// <summary>
    /// The file holding the gzip-compressed ownership XML. Null when no XML was
    /// captured (status <see cref="InsiderFilingCaptureStatus.NotChecked"/> or
    /// <see cref="InsiderFilingCaptureStatus.NotPresent"/>). The stored
    /// (compressed) size is <c>Content.Size</c>; the pre-compression size is
    /// <see cref="UncompressedSize"/>.
    /// </summary>
    public Guid? ContentId { get; set; }
    public virtual File Content { get; set; }

    /// <summary>Size in bytes of the ownership XML before gzip compression. Null when none captured.</summary>
    public long? UncompressedSize { get; set; }

    public InsiderFilingCaptureStatus CaptureStatus { get; set; } =
        InsiderFilingCaptureStatus.NotChecked;

    /// <summary>
    /// How many times a backfill has tried (and failed to reach a terminal
    /// result) to capture this filing's XML. Lets a permanently-unfetchable
    /// filing stop starving the backfill queue. Only meaningful while
    /// <see cref="CaptureStatus"/> is <see cref="InsiderFilingCaptureStatus.NotChecked"/>.
    /// </summary>
    public int CaptureAttempts { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
