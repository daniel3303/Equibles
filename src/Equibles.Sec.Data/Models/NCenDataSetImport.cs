using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// Ledger of SEC's quarterly Form N-CEN data sets already imported. A quarter is imported again
/// when SEC re-posts it under another file name or the parser version rises.
/// </summary>
[Index(nameof(Year), nameof(Quarter), IsUnique = true)]
public class NCenDataSetImport
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public int Year { get; set; }

    /// <summary>Calendar quarter of the data set, 1 to 4.</summary>
    public int Quarter { get; set; }

    /// <summary>The archive's file name on SEC's catalogue, e.g. "2025q2_ncen_0.zip".</summary>
    [MaxLength(128)]
    public string FileName { get; set; }

    public int ParserVersion { get; set; }

    /// <summary>Annual reports (N-CEN and N-CEN/A accessions) stored from this data set.</summary>
    public int ReportCount { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
