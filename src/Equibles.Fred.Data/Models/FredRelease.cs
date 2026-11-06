using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Fred.Data.Models;

[Index(nameof(ReleaseId), IsUnique = true)]
[Index(nameof(Source), nameof(SourceKey), IsUnique = true)]
[Index(nameof(CountryCode))]
public class FredRelease
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // FRED's numeric release id (e.g. 10 = "Consumer Price Index"); null for another publisher's release.
    public int? ReleaseId { get; set; }

    [Required]
    [MaxLength(2)]
    public string CountryCode { get; set; } = "US";

    public IndicatorSource Source { get; set; } = IndicatorSource.Fred;

    // The name a publisher other than FRED states the release by.
    [MaxLength(256)]
    public string SourceKey { get; set; }

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
