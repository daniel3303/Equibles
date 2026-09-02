using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// One named artifact inside an EDGAR submission: the primary form, an exhibit, or another
/// document block. The parent filing remains <see cref="Document"/>; this row preserves the
/// per-file boundary and source metadata needed to cite a specific legal agreement.
/// </summary>
[Index(nameof(DocumentId), nameof(FileName), IsUnique = true)]
[Index(nameof(Type))]
public class SecFilingArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DocumentId { get; set; }
    public virtual Document Document { get; set; }

    [Required]
    [MaxLength(256)]
    public string FileName { get; set; }

    [Required]
    [MaxLength(64)]
    public string Type { get; set; }

    [MaxLength(32)]
    public string Sequence { get; set; }

    /// <summary>
    /// Parsed ordinal used only for display ordering. <see cref="Sequence"/> retains the exact
    /// source value, including uncommon non-numeric values.
    /// </summary>
    public int? SequenceNumber { get; set; }

    [MaxLength(512)]
    public string Description { get; set; }

    [MaxLength(20)]
    public string FilerCik { get; set; }

    [Required]
    [MaxLength(512)]
    public string SourceUrl { get; set; }

    public bool IsPrimary { get; set; }

    [Required]
    public SecFilingArtifactCaptureStatus CaptureStatus { get; set; }

    /// <summary>
    /// Normalized Markdown for HTML/TXT artifacts. Binary artifacts keep this null and retain
    /// their canonical SEC URL so a bounded parser can capture them later.
    /// </summary>
    public string Content { get; set; }

    [MaxLength(64)]
    public string ContentSha256 { get; set; }

    public long? ContentLength { get; set; }

    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}
