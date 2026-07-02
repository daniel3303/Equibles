using System.ComponentModel.DataAnnotations;

namespace Equibles.Media.Data.Models;

/// <summary>
/// A filesystem blob whose owning <see cref="File"/> row was deleted, queued for the
/// deletion sweep. The blob cannot be unlinked inline: the store is content-addressed,
/// so another File row may share the same bytes, and a delete can race an identical
/// re-upload that dedup-skips onto the existing file. The sweep re-checks references
/// after a grace period and retires the blob through a reversible trash phase.
/// Duplicate rows for the same hash are allowed — the sweep is idempotent.
/// </summary>
public class PendingBlobDeletion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The deleted row's algorithm-prefixed content hash, e.g. "sha256:…".</summary>
    [Required]
    [StringLength(80)]
    public string ContentHash { get; set; }

    /// <summary>The deleted row's store-relative blob path, e.g. "blob/sha256/a7/f3/…".</summary>
    [Required]
    [StringLength(128)]
    public string RelativePath { get; set; }

    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
}
