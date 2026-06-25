using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// An image asset referenced by a filing's as-filed HTML — an 8-K investor-deck slide, a logo, a
/// figure — downloaded from EDGAR at capture time and stored so the document viewer can serve it
/// from our own origin. SEC 403s browser hotlinking, so a filing's relative <c>&lt;img src&gt;</c>
/// can never load in the viewer's sandboxed iframe; this lets the viewer rewrite each image to a
/// same-origin proxy that streams the stored bytes.
/// </summary>
/// <remarks>
/// Linked to its owning <see cref="Document"/> with a cascade FK, so the link rows go when the
/// document does. The underlying <see cref="Media.Data.Models.File"/> blob is cleaned up on the
/// same application delete path as the document's other artifacts (Content / XBRL / as-filed HTML).
/// </remarks>
[Index(nameof(DocumentId), nameof(FileName), IsUnique = true)]
public class DocumentImage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DocumentId { get; set; }
    public virtual Document Document { get; set; }

    /// <summary>
    /// The image's original relative filename as referenced by the exhibit's <c>&lt;img src&gt;</c>
    /// (e.g. <c>ebs2026-03x31deck001.jpg</c>). The viewer matches an exhibit image to its stored
    /// blob by (document, filename), so this is the lookup key — always a bare EDGAR filename, and
    /// required (a null could never match a rewritten <c>&lt;img src&gt;</c>). The stitcher only
    /// surfaces names within this length, so the stored key equals the name in the as-filed HTML.
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string FileName { get; set; }

    /// <summary>The stored image blob (bytes in <see cref="Media.Data.Models.FileContent"/>).</summary>
    public Guid FileId { get; set; }

    [Required]
    public virtual File File { get; set; }
}
