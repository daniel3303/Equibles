using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// EDGAR's own description of one filer from its submissions header: SEC's entity type, SIC
/// industry and filer category. Kept for filers that are not tracked issuers, such as 13F
/// managers, so their regulatory classification is available without a name heuristic.
/// </summary>
[Index(nameof(Cik), IsUnique = true)]
[Index(nameof(Sic))]
public class SecFilerProfile
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The filer's CIK without leading zeros.</summary>
    [Required]
    [MaxLength(16)]
    public string Cik { get; set; }

    /// <summary>False when EDGAR has no submissions record for the CIK; every other field is then null.</summary>
    public bool Found { get; set; }

    [MaxLength(512)]
    public string Name { get; set; }

    /// <summary>SEC's entity type, e.g. "operating" or "other".</summary>
    [MaxLength(32)]
    public string EntityType { get; set; }

    /// <summary>SEC's four-digit Standard Industrial Classification code; null when SEC assigned none.</summary>
    [MaxLength(8)]
    public string Sic { get; set; }

    [MaxLength(256)]
    public string SicDescription { get; set; }

    /// <summary>SEC's filer status as published, e.g. "Non-accelerated filer&lt;br&gt;Smaller reporting company" for two.</summary>
    [MaxLength(128)]
    public string Category { get; set; }

    [MaxLength(16)]
    public string StateOfIncorporation { get; set; }

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
