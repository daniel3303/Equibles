using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// A raw XBRL envelope persisted at filing-ingest time so the dimensional-fact
/// extractors can read it later without re-downloading the original filing. The
/// bytes are gzip-compressed; <see cref="UncompressedSize"/> and
/// <see cref="CompressedSize"/> are kept so storage can be sized before any
/// large historical backfill. Each artifact is attributed to the issuer's
/// <see cref="CommonStock"/> and is unique per accession + artifact type.
/// </summary>
[Index(nameof(AccessionNumber), nameof(ArtifactType), IsUnique = true)]
[Index(nameof(CommonStockId))]
public class RawFilingArtifact
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    /// <summary>The SEC accession number of the filing this artifact came from.</summary>
    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    /// <summary>Whether this is the inline iXBRL envelope or a standalone XBRL instance.</summary>
    public RawFilingArtifactType ArtifactType { get; set; }

    /// <summary>
    /// The artifact's file name as listed by EDGAR (e.g. <c>aapl-20180929.xml</c>),
    /// or the primary document name for inline iXBRL.
    /// </summary>
    [MaxLength(256)]
    public string SourceFileName { get; set; }

    /// <summary>The gzip-compressed raw envelope bytes.</summary>
    public byte[] Content { get; set; }

    /// <summary>Size in bytes of the envelope before compression.</summary>
    public long UncompressedSize { get; set; }

    /// <summary>Size in bytes of the stored (gzip-compressed) <see cref="Content"/>.</summary>
    public long CompressedSize { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
