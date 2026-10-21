using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// One accession a bulk data set import committed, written in the accession's own flush
/// transaction, so an import interrupted by a restart resumes after it instead of flushing the
/// whole archive again. A row speaks only for the archive and parser version it was written
/// under; the scraper clears a data set's rows once the data set completes.
/// </summary>
[PrimaryKey(nameof(FileName), nameof(AccessionNumber))]
public class ProcessedDataSetAccession
{
    [MaxLength(128)]
    public string FileName { get; set; }

    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    public int ParserVersion { get; set; }

    [Required]
    [MaxLength(64)]
    public string ArchiveFingerprint { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
