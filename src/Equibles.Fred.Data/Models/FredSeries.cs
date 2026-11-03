using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Fred.Data.Models;

[Index(nameof(SeriesId), IsUnique = true)]
[Index(nameof(Category))]
[Index(nameof(CountryCode))]
public class FredSeries
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(64)]
    public string SeriesId { get; set; }

    [Required]
    [MaxLength(256)]
    public string Title { get; set; }

    public FredSeriesCategory Category { get; set; }

    // The economy the series measures, ISO 3166 alpha-2. US-wide readings never mix in another country's series.
    [Required]
    [MaxLength(2)]
    public string CountryCode { get; set; } = "US";

    public IndicatorSource Source { get; set; } = IndicatorSource.Fred;

    // The publisher's own id when it is not FRED: a Valet series name or a Statistics Canada vector id.
    [MaxLength(64)]
    public string SourceKey { get; set; }

    // The publisher's page for the series; FRED's is derived from the id.
    [MaxLength(256)]
    public string SourceUrl { get; set; }

    [MaxLength(32)]
    public string Frequency { get; set; }

    [MaxLength(128)]
    public string Units { get; set; }

    [MaxLength(64)]
    public string SeasonalAdjustment { get; set; }

    public DateOnly? ObservationStart { get; set; }
    public DateOnly? ObservationEnd { get; set; }

    public DateTime? LastUpdated { get; set; }

    public Guid? FredReleaseId { get; set; }
    public virtual FredRelease FredRelease { get; set; }

    public virtual ICollection<FredObservation> Observations { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
