using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// Ledger of SEC's quarterly Form N-CEN data sets. A quarter is imported again when SEC re-posts it
/// under another file name or the parser version rises; a quarter that failed waits a day.
/// </summary>
[Index(nameof(Year), nameof(Quarter), IsUnique = true)]
public class NCenDataSetImport
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public int Year { get; set; }

    /// <summary>Calendar quarter of the data set, 1 to 4.</summary>
    public int Quarter { get; set; }

    /// <summary>The imported archive's file name, e.g. "2025q2_ncen_0.zip"; null until one imports.</summary>
    [MaxLength(128)]
    public string FileName { get; set; }

    public int ParserVersion { get; set; }

    /// <summary>Annual reports (N-CEN and N-CEN/A accessions) stored from this data set.</summary>
    public int ReportCount { get; set; }

    public DateTime? ImportedAt { get; set; }

    /// <summary>When the last attempt failed; cleared by the next successful import.</summary>
    public DateTime? LastFailureAt { get; set; }

    [MaxLength(1024)]
    public string LastFailure { get; set; }
}
