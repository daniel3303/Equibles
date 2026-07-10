using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Fred.Data.Models;

[Index(nameof(ReleaseId), IsUnique = true)]
public class FredRelease
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // FRED's numeric release id (e.g. 10 = "Consumer Price Index").
    public int ReleaseId { get; set; }

    [Required]
    [MaxLength(256)]
    public string Name { get; set; }

    [MaxLength(512)]
    public string Link { get; set; }

    public bool PressRelease { get; set; }

    // How market-moving this release's prints are; stamped by the calendar importer
    // from the curated per-release map (unmapped releases stay Low).
    public FredReleaseImportance Importance { get; set; }

    public virtual ICollection<FredSeries> Series { get; set; } = [];

    public virtual ICollection<FredReleaseDate> ReleaseDates { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
