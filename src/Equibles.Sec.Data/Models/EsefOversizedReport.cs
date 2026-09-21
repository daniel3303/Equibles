using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// A European annual report larger than the ceiling the extraction sweep parses, remembered so its bytes
/// are not fetched again: the filing index's host states no content length, so the refusal costs the
/// ceiling's worth of transfer every time it is reached. The row records the ceiling the report exceeded
/// rather than its size, which is never learned, so raising that ceiling re-opens every filing refused
/// under a lower one.
/// </summary>
public class EsefOversizedReport
{
    /// <summary>
    /// The filing's reference, the same <c>{LEI}-{yyyyMMdd}-{CC}</c> the stored document would carry, so
    /// the issuer and the period are readable from the key itself. A correction refiled for the same
    /// period and country keeps that reference, exactly as a stored document does, and is not re-read.
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

    [MaxLength(1000)]
    public string HtmlSourceUrl { get; set; }

    public int? HtmlCeilingBytes { get; set; }

    public DateTime RefusedAt { get; set; }
}
