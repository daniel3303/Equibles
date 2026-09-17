using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// A European annual report larger than the ceiling the extraction sweep parses, remembered so its bytes
/// are never fetched again. The filing index's host states no content length and serves the report
/// chunked, so the refusal is only reached after the whole body has been read; without this row the same
/// reports are downloaded and discarded every cycle. It records the ceiling the report exceeded rather
/// than its size, which is never learned, so raising that ceiling re-opens every filing refused under a
/// lower one.
/// </summary>
public class EsefOversizedReport
{
    /// <summary>
    /// The filing's reference, the same <c>{LEI}-{yyyyMMdd}-{CC}</c> the stored document would carry, so
    /// the issuer and the period are readable from the key itself.
    /// </summary>
    [Key]
    [MaxLength(32)]
    public string Reference { get; set; }

    /// <summary>The address the report was read from, kept as the evidence for the refusal.</summary>
    [Required]
    [MaxLength(1000)]
    public string SourceUrl { get; set; }

    /// <summary>The ceiling in force when the report was refused, in bytes.</summary>
    public int CeilingBytes { get; set; }

    public DateTime RefusedAt { get; set; }
}
